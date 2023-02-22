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

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
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
                    8623,   // The query processor ran out of internal resources and could not produce a query plan.
            };

        private readonly ISqlConnectionBuilder _sqlConnectionBuilder;
        private readonly IsExceptionRetriable _defaultIsExceptionRetriable = DefaultIsExceptionRetriable;
        private readonly bool _defaultIsExceptionRetriableOff;
        private readonly IsExceptionRetriable _customIsExceptionRetriable;
        private readonly int _maxRetries;
        private readonly int _retryMillisecondsDelay;

        public SqlRetryService(
            ISqlConnectionBuilder sqlConnectionBuilder,
            IOptions<SqlRetryServiceOptions> sqlRetryServiceOptions,
            SqlRetryServiceDelegateOptions sqlRetryServiceDelegateOptions)
        {
            EnsureArg.IsNotNull(sqlConnectionBuilder, nameof(sqlConnectionBuilder));
            EnsureArg.IsNotNull(sqlRetryServiceOptions?.Value, nameof(sqlRetryServiceOptions));
            EnsureArg.IsNotNull(sqlRetryServiceDelegateOptions, nameof(sqlRetryServiceDelegateOptions));

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

        public delegate bool IsExceptionRetriable(Exception ex);

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
                    if (!RetryTest(ex) || ++retry >= _maxRetries)
                    {
                        throw;
                    }
                }

                await Task.Delay(_retryMillisecondsDelay, cancellationToken);
            }
        }

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
                    using SqlConnection sqlConnection = await _sqlConnectionBuilder.GetSqlConnectionAsync(initialCatalog: null, cancellationToken: cancellationToken).ConfigureAwait(false); // TODO: check for transaction

                    // WARNING, this code will not set sqlCommand.Transaction. Sql transactions via C#/.NET are not supported in this method.
                    // NOTE: connection is created by SqlConnectionHelper.GetBaseSqlConnectionAsync differently, depending on the _sqlConnectionBuilder implementation.
                    // Connection is never open but RetryLogicProvider is set to the old retry implementation. According to the .NET spec, RetryLogicProvider must be set before opening connection to take effect.
                    // Therefore we must reset it to null here before opening the connection.
                    sqlConnection.RetryLogicProvider = null; // Before opening connection, reset old retry logic to null! To remove this line _sqlConnectionBuilder in healthcare-shared-components must be modified.
                    await sqlConnection.OpenAsync(cancellationToken);

                    // sqlCommand.CommandTimeout = (int)_sqlServerDataStoreConfiguration.CommandTimeout.TotalSeconds; // TODO:
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

        public async Task<List<TResult>> ExecuteSqlDataReader<TResult, TLogger>(SqlCommand sqlCommand, Func<SqlDataReader, TResult> readerToResult, ILogger<TLogger> logger, string logMessage, CancellationToken cancellationToken)
        {
            return await ExecuteSqlDataReader(sqlCommand, readerToResult, logger, logMessage, true, cancellationToken);
        }

        public async Task<TResult> ExecuteSqlDataReaderFirstRow<TResult, TLogger>(SqlCommand sqlCommand, Func<SqlDataReader, TResult> readerToResult, ILogger<TLogger> logger, string logMessage, CancellationToken cancellationToken)
            where TResult : class
        {
            List<TResult> result = await ExecuteSqlDataReader(sqlCommand, readerToResult, logger, logMessage, false, cancellationToken);
            return result.Count > 0 ? result[0] : null;
        }
    }
}
