// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
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

        private ISqlConnectionBuilder _sqlConnectionBuilder;
        private readonly IsExceptionRetriable _defaultIsExceptionRetriable = DefaultIsExceptionRetriable;
        private readonly bool _defaultIsExceptionRetriableOff;
        private readonly IsExceptionRetriable _customIsExceptionRetriable;
        private int _maxRetries;
        private int _retryMillisecondsDelay;
        private int _commandTimeout;
        private static ReplicaHandler _replicaHandler;
        private static object _initLocker = new object();
        private static EventLogHandler _eventLogHandler;

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
            _commandTimeout = (int)EnsureArg.IsNotNull(sqlServerDataStoreConfiguration?.Value, nameof(sqlServerDataStoreConfiguration)).CommandTimeout.TotalSeconds;

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

            InitReplicaHandler();
            InitEventLogHandler();
        }

        private SqlRetryService(ISqlConnectionBuilder sqlConnectionBuilder)
        {
            _sqlConnectionBuilder = sqlConnectionBuilder;
            InitReplicaHandler();
            InitEventLogHandler();
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
        /// Simplified class generator.
        /// </summary>
        /// <param name="sqlConnectionBuilder">Internal FHIR server interface used to create SqlConnection.</param>
        /// <param name="commandTimeout">command timeout.</param>
        /// <param name="maxRetries">max retries.</param>
        /// <param name="retryMillisecondsDelay">retry milliseconds delay.</param>
        public static SqlRetryService GetInstance(ISqlConnectionBuilder sqlConnectionBuilder, int commandTimeout = 300, int maxRetries = 5, int retryMillisecondsDelay = 5000)
        {
            EnsureArg.IsNotNull(sqlConnectionBuilder, nameof(sqlConnectionBuilder));
            var service = new SqlRetryService(sqlConnectionBuilder);
            service._commandTimeout = commandTimeout;
            service._maxRetries = maxRetries;
            service._retryMillisecondsDelay = retryMillisecondsDelay;
            return service;
        }

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

        private bool IsRetriable(Exception ex)
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
        /// <param name="logger">Logger used on first try error (or retry error) and connection open.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <param name="isReadOnly">"Flag indicating whether connection to read only replica can be used."</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        /// <exception>When executing this method, if exception is thrown that is not retriable or if last retry fails, then same exception is thrown by this method.</exception>
        public async Task ExecuteSql(Func<SqlConnection, CancellationToken, SqlException, Task> action, ILogger logger, CancellationToken cancellationToken, bool isReadOnly = false)
        {
            EnsureArg.IsNotNull(action, nameof(action));

            SqlException sqlException = null;

            int retry = 0;
            while (true)
            {
                try
                {
                    using SqlConnection sqlConnection = await _replicaHandler.GetConnection(_sqlConnectionBuilder, isReadOnly, logger, cancellationToken);
                    await action(sqlConnection, cancellationToken, sqlException);
                    return;
                }
                catch (Exception ex)
                {
                    if (++retry >= _maxRetries || !IsRetriable(ex))
                    {
                        throw;
                    }

                    if (ex is SqlException sqlEx)
                    {
                        sqlException = sqlEx;
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
        /// <param name="sqlCommand">SQL command to be executed.</param>
        /// <param name="action">Delegate to be executed by passing <paramref name="sqlCommand"/> as input parameter.</param>
        /// <param name="logger">Logger used on first try error (or retry error) and connection open.</param>
        /// <param name="logMessage">Message to be logged on error.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <param name="isReadOnly">"Flag indicating whether connection to read only replica can be used."</param>
        /// <param name="disableRetries">"Flag indicating whether retries are disabled."</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        /// <exception>When executing this method, if exception is thrown that is not retriable or if last retry fails, then same exception is thrown by this method.</exception>
        public async Task ExecuteSql(SqlCommand sqlCommand, Func<SqlCommand, CancellationToken, Task> action, ILogger logger, string logMessage, CancellationToken cancellationToken, bool isReadOnly = false, bool disableRetries = false)
        {
            EnsureArg.IsNotNull(sqlCommand, nameof(sqlCommand));
            EnsureArg.IsNotNull(action, nameof(action));
            EnsureArg.IsNotNull(logger, nameof(logger));
            if (logMessage == null)
            {
                logMessage = $"{sqlCommand.CommandText} failed.";
            }

            var start = DateTime.UtcNow;
            Exception lastException = null;
            int retry = 0;
            while (true)
            {
                try
                {
                    using SqlConnection sqlConnection = await _replicaHandler.GetConnection(_sqlConnectionBuilder, isReadOnly, logger, cancellationToken);
                    //// only change if not default 30 seconds. This should allow to handle any explicitly set timeouts correctly.
                    sqlCommand.CommandTimeout = sqlCommand.CommandTimeout == 30 ? _commandTimeout : sqlCommand.CommandTimeout;
                    sqlCommand.Connection = sqlConnection;

                    await action(sqlCommand, cancellationToken);
                    if (retry > 0)
                    {
                        await TryLogEvent($"SuccessOnRetry:{sqlCommand.CommandText}", "Warn", $"retries={retry} error={lastException}", start, cancellationToken);
                    }

                    return;
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    if (disableRetries || !IsRetriable(ex))
                    {
                        throw;
                    }

                    if (++retry >= _maxRetries)
                    {
                        logger.LogError(ex, $"Final attempt ({retry}): {logMessage}");
                        await TryLogEvent($"Retry:{sqlCommand.CommandText}", "Error", $"retries={retry} error={lastException}", start, cancellationToken);
                        throw;
                    }

                    logger.LogInformation(ex, $"Attempt {retry}: {logMessage}");
                }

                await Task.Delay(_retryMillisecondsDelay, cancellationToken);
            }
        }

        private async Task<IReadOnlyList<TResult>> ExecuteSqlDataReaderAsync<TResult>(SqlCommand sqlCommand, Func<SqlDataReader, TResult> readerToResult, ILogger logger, string logMessage, bool allRows, bool isReadOnly, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(sqlCommand, nameof(sqlCommand));
            EnsureArg.IsNotNull(readerToResult, nameof(readerToResult));
            EnsureArg.IsNotNull(logger, nameof(logger));

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
                cancellationToken,
                isReadOnly);

            return results;
        }

        /// <summary>
        /// Executes <paramref name="sqlCommand"/> and reads all the rows. Translates the read rows by using <paramref name="readerToResult"/>
        /// into the <typeparamref name="TResult"/> data type and returns them. Retries execution of <paramref name="sqlCommand"/> on SQL error or failed
        /// SQL connection error. In the case if non-retriable exception or if the last retry failed tha same exception is thrown.
        /// </summary>
        /// <typeparam name="TResult">Defines data type for the returned SQL rows.</typeparam>
        /// <param name="sqlCommand">SQL command to be executed.</param>
        /// <param name="readerToResult">Translation delegate that translates the row returned by <paramref name="sqlCommand"/> execution into the <typeparamref name="TResult"/> data type.</param>
        /// <param name="logger">Logger used on first try error or retry error.</param>
        /// <param name="logMessage">Message to be logged on error.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <param name="isReadOnly">"Flag indicating whether connection to read only replica can be used."</param>
        /// <returns>A task representing the asynchronous operation that returns all the rows that result from <paramref name="sqlCommand"/> execution. The rows are translated by <paramref name="readerToResult"/> delegate
        /// into <typeparamref name="TResult"/> data type.</returns>
        /// <exception>When executing this method, if exception is thrown that is not retriable or if last retry fails, then same exception is thrown by this method.</exception>
        public async Task<IReadOnlyList<TResult>> ExecuteReaderAsync<TResult>(SqlCommand sqlCommand, Func<SqlDataReader, TResult> readerToResult, ILogger logger, string logMessage, CancellationToken cancellationToken, bool isReadOnly = false)
        {
            return await ExecuteSqlDataReaderAsync(sqlCommand, readerToResult, logger, logMessage, true, isReadOnly, cancellationToken);
        }

        /// <summary>
        /// Tries logging an event to the EventLog table.
        /// </summary>
        /// <param name="process">Name of the process.</param>
        /// <param name="status">Status. By default Warn and Error are logged automatically. Other stuses can be enabled in the Parameters table.</param>
        /// <param name="text">Message text.</param>
        /// <param name="startDate">Optional start date of the process.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async Task TryLogEvent(string process, string status, string text, DateTime? startDate, CancellationToken cancellationToken)
        {
            try
            {
                await using var cmd = new SqlCommand { CommandType = CommandType.StoredProcedure, CommandText = "dbo.LogEvent" };
                cmd.Parameters.AddWithValue("@Process", process);
                cmd.Parameters.AddWithValue("@Status", status);
                cmd.Parameters.AddWithValue("@Text", text);
                if (startDate.HasValue)
                {
                    cmd.Parameters.AddWithValue("@Start", startDate.Value);
                }

                var connStr = _eventLogHandler.GetEventLogConnectionString(_sqlConnectionBuilder);
                if (connStr == null)
                {
                    using var conn = await _sqlConnectionBuilder.GetSqlConnectionAsync(initialCatalog: null, cancellationToken: cancellationToken).ConfigureAwait(false);
                    conn.RetryLogicProvider = null;
                    await conn.OpenAsync(cancellationToken);
                    cmd.Connection = conn;
                    await cmd.ExecuteNonQueryAsync(cancellationToken);
                }
                else
                {
                    using var conn = new SqlConnection(connStr);
                    conn.RetryLogicProvider = null;
                    await conn.OpenAsync(cancellationToken);
                    cmd.Connection = conn;
                    await cmd.ExecuteNonQueryAsync(cancellationToken);
                }
            }
            catch
            {
                // do nothing;
            }
        }

        private static void InitReplicaHandler()
        {
            if (_replicaHandler == null) // this is needed, strictly speaking, only if SqlRetryService is not singleton, but it works either way.
            {
                lock (_initLocker)
                {
                    _replicaHandler ??= new ReplicaHandler();
                }
            }
        }

        private static void InitEventLogHandler()
        {
            if (_eventLogHandler == null) // this is needed, strictly speaking, only if SqlRetryService is not singleton, but it works either way.
            {
                lock (_initLocker)
                {
                    _eventLogHandler ??= new EventLogHandler();
                }
            }
        }

        private class ReplicaHandler
        {
            private DateTime? _lastUpdated;
            private readonly object _databaseAccessLocker = new object();
            private double _replicaTrafficRatio = 0;
            private long _usageCounter = 0;

            public ReplicaHandler()
            {
            }

            public async Task<SqlConnection> GetConnection(ISqlConnectionBuilder sqlConnectionBuilder, bool isReadOnly, ILogger logger, CancellationToken cancel)
            {
                SqlConnection conn;
                var sw = Stopwatch.StartNew();
                var isReadOnlyConnection = isReadOnly ? "read-only " : string.Empty;
                if (!isReadOnly)
                {
                    conn = await sqlConnectionBuilder.GetSqlConnectionAsync(initialCatalog: null, cancellationToken: cancel).ConfigureAwait(false);
                }
                else
                {
                    var replicaTrafficRatio = GetReplicaTrafficRatio(sqlConnectionBuilder);

                    if (replicaTrafficRatio < 0.5) // it does not make sense to use replica less than master at all
                    {
                        isReadOnlyConnection = string.Empty;
                        conn = await sqlConnectionBuilder.GetSqlConnectionAsync(initialCatalog: null, cancellationToken: cancel).ConfigureAwait(false);
                    }
                    else if (replicaTrafficRatio > 0.99)
                    {
                        conn = await sqlConnectionBuilder.GetReadOnlySqlConnectionAsync(initialCatalog: null, cancellationToken: cancel).ConfigureAwait(false);
                    }
                    else
                    {
                        var useWriteConnection = unchecked(Interlocked.Increment(ref _usageCounter)) % (int)(1 / (1 - _replicaTrafficRatio)) == 1; // examples for ratio -> % divider = { 0.9 -> 10, 0.8 -> 5, 0.75 - 4, 0.67 - 3, 0.5 -> 2, <0.5 -> 1}
                        if (useWriteConnection)
                        {
                            isReadOnlyConnection = string.Empty;
                        }

                        conn = useWriteConnection
                                ? await sqlConnectionBuilder.GetSqlConnectionAsync(initialCatalog: null, cancellationToken: cancel).ConfigureAwait(false)
                                : await sqlConnectionBuilder.GetReadOnlySqlConnectionAsync(initialCatalog: null, cancellationToken: cancel).ConfigureAwait(false);
                    }
                }

                // Connection is never opened by the _sqlConnectionBuilder but RetryLogicProvider is set to the old, deprecated retry implementation. According to the .NET spec, RetryLogicProvider
                // must be set before opening connection to take effect. Therefore we must reset it to null here before opening the connection.
                conn.RetryLogicProvider = null; // To remove this line _sqlConnectionBuilder in healthcare-shared-components must be modified.
                await conn.OpenAsync(cancel);
                logger.LogInformation($"Opened {isReadOnlyConnection}connection to the database in {sw.Elapsed.TotalSeconds} seconds.");

                return conn;
            }

            private double GetReplicaTrafficRatio(ISqlConnectionBuilder sqlConnectionBuilder)
            {
                const int trafficRatioCacheDurationSec = 600;
                if (_lastUpdated.HasValue && (DateTime.UtcNow - _lastUpdated.Value).TotalSeconds < trafficRatioCacheDurationSec)
                {
                    return _replicaTrafficRatio;
                }

                lock (_databaseAccessLocker)
                {
                    if (_lastUpdated.HasValue && (DateTime.UtcNow - _lastUpdated.Value).TotalSeconds < trafficRatioCacheDurationSec)
                    {
                        return _replicaTrafficRatio;
                    }

                    _replicaTrafficRatio = GetReplicaTrafficRatioFromDatabase(sqlConnectionBuilder);
                    _lastUpdated = DateTime.UtcNow;
                }

                return _replicaTrafficRatio;
            }

            private static double GetReplicaTrafficRatioFromDatabase(ISqlConnectionBuilder sqlConnectionBuilder)
            {
                try
                {
                    using var conn = sqlConnectionBuilder.GetSqlConnection();
                    conn.RetryLogicProvider = null;
                    conn.Open();
                    using var cmd = new SqlCommand("IF object_id('dbo.Parameters') IS NOT NULL SELECT Number FROM dbo.Parameters WHERE Id = 'ReplicaTrafficRatio'", conn);
                    var value = cmd.ExecuteScalar();
                    return value == null ? 0 : (double)value;
                }
                catch (SqlException)
                {
                    return 0;
                }
            }
        }

        private class EventLogHandler
        {
            private bool _initialized = false;
            private readonly object _databaseAccessLocker = new object();
            private string _eventLogConnectionString = null;

            public EventLogHandler()
            {
            }

            internal string GetEventLogConnectionString(ISqlConnectionBuilder sqlConnectionBuilder)
            {
                if (!_initialized)
                {
                    lock (_databaseAccessLocker)
                    {
                        if (!_initialized)
                        {
                            _eventLogConnectionString = GetEventLogConnectionStringFromDatabase(sqlConnectionBuilder);
                        }
                    }
                }

                return _eventLogConnectionString;
            }

            private string GetEventLogConnectionStringFromDatabase(ISqlConnectionBuilder sqlConnectionBuilder)
            {
                try
                {
                    using var conn = sqlConnectionBuilder.GetSqlConnection();
                    conn.RetryLogicProvider = null;
                    conn.Open();
                    using var cmd = new SqlCommand("IF object_id('dbo.Parameters') IS NOT NULL SELECT Char FROM dbo.Parameters WHERE Id = 'EventLogConnectionString'", conn);
                    var value = cmd.ExecuteScalar();
                    var result = value == null ? null : (string)value;
                    _initialized = true;
                    return result;
                }
                catch (SqlException)
                {
                    return null;
                }
            }
        }
    }
}
