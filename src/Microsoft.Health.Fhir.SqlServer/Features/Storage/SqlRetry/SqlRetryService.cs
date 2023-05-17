// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.SqlServer;
using Microsoft.Health.SqlServer.Configs;
using Microsoft.Health.SqlServer.Features.Storage;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    /// <summary>
    /// <para>This class implements ISqlRetryService interface that is used to execute retriable SQL commands. If SQL command fails
    /// either because of a connection error or SQL error and the error is determined to be retriable, then methods in this class
    /// will retry the command.</para>
    /// <para>This class is designed to operate as .NET service and should be initialized as such in a standard .NET way, for
    /// example in Startup.cs or equivalent source file.</para>
    /// <para>This class does not support Microsoft.Data.SqlClient.SqlTransaction class used to execute SQL transactions. However,
    /// SQL transactions can still be executed if, for example, defined in the SqlCommand.CommandText property of the SqlCommand
    /// that is passed as a parameter when calling one of the methods of this class.</para>
    /// </summary>
    public class SqlRetryService : ISqlRetryService
    {
        // Default errors copied from src/Microsoft.Data.SqlClient/src/Microsoft/Data/SqlClient/Reliability/SqlConfigurableRetryFactory.cs .
        private readonly HashSet<int> _transientErrors
            = new()
            {
                // Default .NET errors:
                    1204,   // The instance of the SQL Server Database Engine cannot obtain a LOCK resource at this time. Rerun your statement when there are fewer active users. Ask the database administrator to check the lock and memory configuration for this instance, or to check for long-running transactions.
                    1205,   // Transaction (Process ID) was deadlocked on resources with another process and has been chosen as the deadlock victim. Rerun the transaction
                    1222,   // Lock request time out period exceeded.
                    49918,  // Cannot process request. Not enough resources to process request.
                    49919,  // Cannot process create or update request. Too many create or update operations in progress for subscription "%ld".
                    49920,  // Cannot process request. Too many operations in progress for subscription "%ld".
                    4060,   // Cannot open database "%.*ls" requested by the login. The login failed.
                    4221,   // Login to read-secondary failed due to long wait on 'HADR_DATABASE_WAIT_FOR_TRANSITION_TO_VERSIONING'. The replica is not available for login because row versions are missing for transactions that were in-flight when the replica was recycled. The issue can be resolved by rolling back or committing the active transactions on the primary replica. Occurrences of this condition can be minimized by avoiding long write transactions on the primary.
                    40143,  // The service has encountered an error processing your request. Please try again.
                    40613,  // Database '%.*ls' on server '%.*ls' is not currently available. Please retry the connection later. If the problem persists, contact customer support, and provide them the session tracing ID of '%.*ls'.
                    40501,  // The service is currently busy. Retry the request after 10 seconds. Incident ID: %ls. Code: %d.
                    40540,  // The service has encountered an error processing your request. Please try again.
                    40197,  // The service has encountered an error processing your request. Please try again. Error code %d.
                    42108,  // Can not connect to the SQL pool since it is paused. Please resume the SQL pool and try again.
                    42109,  // The SQL pool is warming up. Please try again.
                    10929,  // Resource ID: %d. The %s minimum guarantee is %d, maximum limit is %d and the current usage for the database is %d. However, the server is currently too busy to support requests greater than %d for this database. For more information, see http://go.microsoft.com/fwlink/?LinkId=267637. Otherwise, please try again later.
                    10928,  // Resource ID: %d. The %s limit for the database is %d and has been reached. For more information, see http://go.microsoft.com/fwlink/?LinkId=267637.
                    10060,  // An error has occurred while establishing a connection to the server. When connecting to SQL Server, this failure may be caused by the fact that under the default settings SQL Server does not allow remote connections. (provider: TCP Provider, error: 0 - A connection attempt failed because the connected party did not properly respond after a period of time, or established connection failed because connected host has failed to respond.) (Microsoft SQL Server, Error: 10060)
                    997,    // A connection was successfully established with the server, but then an error occurred during the login process. (provider: Named Pipes Provider, error: 0 - Overlapped I/O operation is in progress)
                    233,    // A connection was successfully established with the server, but then an error occurred during the login process. (provider: Shared Memory Provider, error: 0 - No process is on the other end of the pipe.) (Microsoft SQL Server, Error: 233)

                // Additional Fhir Server errors:
                    SqlErrorCodes.QueryProcessorNoQueryPlan,   // The query processor ran out of internal resources and could not produce a query plan.
            };

        private readonly ISqlConnectionBuilder _sqlConnectionBuilder;
        private readonly SqlServerDataStoreConfiguration _sqlServerDataStoreConfiguration;
        private readonly IsExceptionRetriable _defaultIsExceptionRetriable = DefaultIsExceptionRetriable;
        private readonly bool _defaultIsExceptionRetriableOff;
        private readonly IsExceptionRetriable _customIsExceptionRetriable;
        private readonly int _maxRetries;
        private readonly int _retryMillisecondsDelay;

        /// <summary>
        /// Constructor that initializes this implementation of the ISqlRetryService interface. This class
        /// is designed to operate as a standard .NET service and all of the parameters to the constructor are passed
        /// using .NET dependency injection.
        /// </summary>
        /// <param name="sqlConnectionBuilder">Internal FHIR server interface used to create SqlConnection.</param>
        /// <param name="sqlServerDataStoreConfiguration">Internal FHIR server interface used initialize this class.</param>
        /// <param name="sqlRetryServiceOptions">Initializes various retry parameters. <see cref="SqlRetryServiceOptions"/></param>
        /// <param name="sqlRetryServiceDelegateOptions">Initializes custom delegate that is used to examine if the thrown exception represent a retriable error. <see cref="SqlRetryServiceDelegateOptions"/></param>
        public SqlRetryService(
            ISqlConnectionBuilder sqlConnectionBuilder,
            IOptions<SqlServerDataStoreConfiguration> sqlServerDataStoreConfiguration,
            IOptions<SqlRetryServiceOptions> sqlRetryServiceOptions,
            SqlRetryServiceDelegateOptions sqlRetryServiceDelegateOptions)
        {
            EnsureArg.IsNotNull(sqlConnectionBuilder, nameof(sqlConnectionBuilder));
            EnsureArg.IsNotNull(sqlRetryServiceOptions?.Value, nameof(sqlRetryServiceOptions));
            EnsureArg.IsNotNull(sqlRetryServiceDelegateOptions, nameof(sqlRetryServiceDelegateOptions));
            _sqlServerDataStoreConfiguration = EnsureArg.IsNotNull(sqlServerDataStoreConfiguration?.Value, nameof(sqlServerDataStoreConfiguration));

            _sqlConnectionBuilder = sqlConnectionBuilder;

            if (sqlRetryServiceOptions.Value.RemoveTransientErrors != null)
            {
                _transientErrors.ExceptWith(sqlRetryServiceOptions.Value.RemoveTransientErrors);
            }

            if (sqlRetryServiceOptions.Value.AddTransientErrors != null)
            {
                _transientErrors.UnionWith(sqlRetryServiceOptions.Value.AddTransientErrors);
            }

            _maxRetries = sqlRetryServiceOptions.Value.MaxRetries;
            _retryMillisecondsDelay = sqlRetryServiceOptions.Value.RetryMillisecondsDelay;

            _defaultIsExceptionRetriableOff = sqlRetryServiceDelegateOptions.DefaultIsExceptionRetriableOff;
            _customIsExceptionRetriable = sqlRetryServiceDelegateOptions.CustomIsExceptionRetriable;
        }

        /// <summary>
        /// Defines a custom delegate that can be used instead of or in addition to IsExceptionRetriable method to examine if thrown
        /// exception <paramref name="ex"/> represent a retriable error.
        /// </summary>
        /// <param name="ex">Exception to be examined.</param>
        /// <returns>Returns true if the exception <paramref name="ex"/> represent an retriable error.</returns>
        /// <see cref="SqlRetryServiceDelegateOptions"/>
        public delegate bool IsExceptionRetriable(Exception ex);

        /// <summary>
        /// This method examines exception <paramref name="ex"/> and determines if the exception represent an retriable error.
        /// In this case the code that caused the exception is executed again.
        /// </summary>
        /// <param name="ex">Exception to be examined.</param>
        /// <returns>Returns true if the exception <paramref name="ex"/> represent an retriable error.</returns>
        private static bool DefaultIsExceptionRetriable(Exception ex)
        {
            if (ex is SqlException sqlEx)
            {
                if (sqlEx.Number == 121 && sqlEx.Message.Contains("an error occurred during the pre-login handshake", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (sqlEx.ToString().Contains("login failed", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private bool RetryTest(Exception ex)
        {
            if (ex is SqlException sqlEx && _transientErrors.Contains(sqlEx.Number))
            {
                return true;
            }

            if (!_defaultIsExceptionRetriableOff && _defaultIsExceptionRetriable(ex))
            {
                return true;
            }

            if (_customIsExceptionRetriable?.Invoke(ex) == true)
            {
                return true;
            }

            if (ex.IsRetriable())
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Executes delegate <paramref name="action"/> and retries it's execution if retriable error is encountered error.
        /// In the case if non-retriable exception or if the last retry failed tha same exception is thrown.
        /// </summary>
        /// <param name="action">Delegate to be executed.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        /// <exception>When executing this method, if exception is thrown that is not retriable or if last retry fails, then same exception is thrown by this method.</exception>
        public async Task ExecuteSql(Func<CancellationToken, Task> action, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(action, nameof(action));

            int retry = 0;
            while (true)
            {
                try
                {
                    await action(cancellationToken);
                    return;
                }
                catch (Exception ex)
                {
                    if (++retry >= _maxRetries || !RetryTest(ex))
                    {
                        throw;
                    }
                }

                await Task.Delay(_retryMillisecondsDelay, cancellationToken);
            }
        }

        /// <summary>
        /// Creates and opens SQL connection and assigns it to <paramref name="sqlCommand"/>. Then executes delegate <paramref name="action"/>
        /// and retries entire process on SQL error or failed SQL connection error. In the case if non-retriable exception or if the last retry failed
        /// tha same exception is thrown.
        /// </summary>
        /// <typeparam name="TLogger">Type used for the <paramref name="logger"/>. <see cref="ILogger{TCategoryName}"/></typeparam>
        /// <param name="sqlCommand">SQL command to be executed.</param>
        /// <param name="action">Delegate to be executed by passing <paramref name="sqlCommand"/> as input parameter.</param>
        /// <param name="logger">Logger used on first try error or retry error.</param>
        /// <param name="logMessage">Message to be logged on error.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        /// <exception>When executing this method, if exception is thrown that is not retriable or if last retry fails, then same exception is thrown by this method.</exception>
        public async Task ExecuteSql<TLogger>(SqlCommand sqlCommand, Func<SqlCommand, CancellationToken, Task> action, ILogger<TLogger> logger, string logMessage, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(sqlCommand, nameof(sqlCommand));
            EnsureArg.IsNotNull(action, nameof(action));
            EnsureArg.IsNotNull(logger, nameof(logger));
            EnsureArg.IsNotNull(logMessage, nameof(logMessage));

            int retry = 0;
            while (true)
            {
                try
                {
                    // NOTE: connection is created by SqlConnectionHelper.GetBaseSqlConnectionAsync differently, depending on the _sqlConnectionBuilder implementation.
                    using SqlConnection sqlConnection = await _sqlConnectionBuilder.GetSqlConnectionAsync(initialCatalog: null, cancellationToken: cancellationToken).ConfigureAwait(false);

                    // Connection is never opened by the _sqlConnectionBuilder but RetryLogicProvider is set to the old, deprecated retry implementation. According to the .NET spec, RetryLogicProvider
                    // must be set before opening connection to take effect. Therefore we must reset it to null here before opening the connection.
                    sqlConnection.RetryLogicProvider = null; // To remove this line _sqlConnectionBuilder in healthcare-shared-components must be modified.
                    await sqlConnection.OpenAsync(cancellationToken);

                    sqlCommand.CommandTimeout = Math.Max(sqlCommand.CommandTimeout, (int)_sqlServerDataStoreConfiguration.CommandTimeout.TotalSeconds);
                    sqlCommand.Connection = sqlConnection;

                    await action(sqlCommand, cancellationToken);
                    return;
                }
                catch (Exception ex)
                {
                    if (!RetryTest(ex))
                    {
                        throw;
                    }

                    if (++retry >= _maxRetries)
                    {
                        logger.LogError(ex, $"Final attempt ({retry}): {logMessage}");
                        throw;
                    }

                    logger.LogInformation(ex, $"Attempt {retry}: {logMessage}");
                }

                await Task.Delay(_retryMillisecondsDelay, cancellationToken);
            }
        }

        private async Task<List<TResult>> ExecuteSqlDataReader<TResult, TLogger>(SqlCommand sqlCommand, Func<SqlDataReader, TResult> readerToResult, ILogger<TLogger> logger, string logMessage, bool allRows, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(sqlCommand, nameof(sqlCommand));
            EnsureArg.IsNotNull(readerToResult, nameof(readerToResult));
            EnsureArg.IsNotNull(logger, nameof(logger));
            EnsureArg.IsNotNull(logMessage, nameof(logMessage));

            List<TResult> results = null;
            await ExecuteSql(
                sqlCommand,
                async (sqlCommand, cancellationToken) =>
                {
                    using SqlDataReader reader = await sqlCommand.ExecuteReaderAsync(cancellationToken);
                    results = new List<TResult>();
                    bool firstRow = true;
                    while (await reader.ReadAsync(cancellationToken) && (allRows || firstRow))
                    {
                        results.Add(readerToResult(reader));
                        firstRow = false;
                    }

                    await reader.NextResultAsync(cancellationToken);
                },
                logger,
                logMessage,
                cancellationToken);

            return results;
        }

        /// <summary>
        /// Executes <paramref name="sqlCommand"/> and reads all the rows. Translates the read rows by using <paramref name="readerToResult"/>
        /// into the <typeparamref name="TResult"/> data type and returns them. Retries execution of <paramref name="sqlCommand"/> on SQL error or failed
        /// SQL connection error. In the case if non-retriable exception or if the last retry failed tha same exception is thrown.
        /// </summary>
        /// <typeparam name="TResult">Defines data type for the returned SQL rows.</typeparam>
        /// <typeparam name="TLogger">Type used for the <paramref name="logger"/>. <see cref="ILogger{TCategoryName}"/></typeparam>
        /// <param name="sqlCommand">SQL command to be executed.</param>
        /// <param name="readerToResult">Translation delegate that translates the row returned by <paramref name="sqlCommand"/> execution into the <typeparamref name="TResult"/> data type.</param>
        /// <param name="logger">Logger used on first try error or retry error.</param>
        /// <param name="logMessage">Message to be logged on error.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task representing the asynchronous operation that returns all the rows that result from <paramref name="sqlCommand"/> execution. The rows are translated by <paramref name="readerToResult"/> delegate
        /// into <typeparamref name="TResult"/> data type.</returns>
        /// <exception>When executing this method, if exception is thrown that is not retriable or if last retry fails, then same exception is thrown by this method.</exception>
        public async Task<List<TResult>> ExecuteSqlDataReader<TResult, TLogger>(SqlCommand sqlCommand, Func<SqlDataReader, TResult> readerToResult, ILogger<TLogger> logger, string logMessage, CancellationToken cancellationToken)
        {
            return await ExecuteSqlDataReader(sqlCommand, readerToResult, logger, logMessage, true, cancellationToken);
        }

        /// <summary>
        /// Executes <paramref name="sqlCommand"/> and reads the first row only. Translates the read row by using <paramref name="readerToResult"/>
        /// into the returned <typeparamref name="TResult"/> data type. Retries execution of <paramref name="sqlCommand"/> on SQL error or failed
        /// SQL connection error. In the case if non-retriable exception or if the last retry failed tha same exception is thrown.
        /// </summary>
        /// <typeparam name="TResult">Defines data type for the returned SQL row.</typeparam>
        /// <typeparam name="TLogger">Type used for the <paramref name="logger"/>. <see cref="ILogger{TCategoryName}"/></typeparam>
        /// <param name="sqlCommand">SQL command to be executed.</param>
        /// <param name="readerToResult">Translation delegate that translates the row returned by <paramref name="sqlCommand"/> execution into the <typeparamref name="TResult"/> data type.</param>
        /// <param name="logger">Logger used on first try error or retry error.</param>
        /// <param name="logMessage">Message to be logged on error.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task representing the asynchronous operation that returns the first row that results from <paramref name="sqlCommand"/> execution. The row is translated by <paramref name="readerToResult"/> delegate
        /// into <typeparamref name="TResult"/> data type.</returns>
        /// <exception>When executing this method, if exception is thrown that is not retriable or if last retry fails, then same exception is thrown by this method.</exception>
        public async Task<TResult> ExecuteSqlDataReaderFirstRow<TResult, TLogger>(SqlCommand sqlCommand, Func<SqlDataReader, TResult> readerToResult, ILogger<TLogger> logger, string logMessage, CancellationToken cancellationToken)
            where TResult : class
        {
            List<TResult> result = await ExecuteSqlDataReader(sqlCommand, readerToResult, logger, logMessage, false, cancellationToken);
            return result.Count > 0 ? result[0] : null;
        }
    }
}
