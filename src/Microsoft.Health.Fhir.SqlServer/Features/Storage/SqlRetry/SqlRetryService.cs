// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
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
        private ISqlConnectionBuilder _sqlConnectionBuilder;
        private readonly IsExceptionRetriable _defaultIsExceptionRetriable = DefaultIsExceptionRetriable;
        private readonly bool _defaultIsExceptionRetriableOff;
        private readonly IsExceptionRetriable _customIsExceptionRetriable;
        private readonly HashSet<int> _transientErrors;
        private int _maxRetries;
        private int _retryMillisecondsDelay;
        private int _commandTimeout;
        private static ReplicaHandler _replicaHandler;
        private static object _initLocker = new object();
        private static EventLogHandler _eventLogHandler;
        private CoreFeatureConfiguration _coreFeatureConfiguration;

        /// <summary>
        /// Constructor that initializes this implementation of the ISqlRetryService interface. This class
        /// is designed to operate as a standard .NET service and all of the parameters to the constructor are passed
        /// using .NET dependency injection.
        /// </summary>
        /// <param name="sqlConnectionBuilder">Internal FHIR server interface used to create SqlConnection.</param>
        /// <param name="sqlServerDataStoreConfiguration">Internal FHIR server interface used initialize this class.</param>
        /// <param name="sqlRetryServiceOptions">Initializes various retry parameters. <see cref="SqlRetryServiceOptions"/></param>
        /// <param name="sqlRetryServiceDelegateOptions">Initializes custom delegate that is used to examine if the thrown exception represent a retriable error. <see cref="SqlRetryServiceDelegateOptions"/></param>
        /// <param name="coreFeatureConfiguration">Checks if SQL replicas are enabled</param>
        public SqlRetryService(
            ISqlConnectionBuilder sqlConnectionBuilder,
            IOptions<SqlServerDataStoreConfiguration> sqlServerDataStoreConfiguration,
            IOptions<SqlRetryServiceOptions> sqlRetryServiceOptions,
            SqlRetryServiceDelegateOptions sqlRetryServiceDelegateOptions,
            IOptions<CoreFeatureConfiguration> coreFeatureConfiguration)
        {
            EnsureArg.IsNotNull(sqlConnectionBuilder, nameof(sqlConnectionBuilder));
            EnsureArg.IsNotNull(sqlRetryServiceOptions?.Value, nameof(sqlRetryServiceOptions));
            EnsureArg.IsNotNull(sqlRetryServiceDelegateOptions, nameof(sqlRetryServiceDelegateOptions));
            EnsureArg.IsNotNull(coreFeatureConfiguration?.Value, nameof(coreFeatureConfiguration));
            _commandTimeout = (int)EnsureArg.IsNotNull(sqlServerDataStoreConfiguration?.Value, nameof(sqlServerDataStoreConfiguration)).CommandTimeout.TotalSeconds;

            _sqlConnectionBuilder = sqlConnectionBuilder;
            _coreFeatureConfiguration = coreFeatureConfiguration.Value;
            _transientErrors = new HashSet<int>(SqlExceptionExtensions.TransientErrors);

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

            InitReplicaHandler(_coreFeatureConfiguration);
            InitEventLogHandler();
        }

        private SqlRetryService(ISqlConnectionBuilder sqlConnectionBuilder)
        {
            _sqlConnectionBuilder = sqlConnectionBuilder;
            _coreFeatureConfiguration = new CoreFeatureConfiguration();
            InitReplicaHandler(_coreFeatureConfiguration);
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
            if (ex is SqlException sqlEx && sqlEx.IsSqlTransientException(_transientErrors))
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
                    using SqlConnection sqlConnection = await _replicaHandler.GetConnection(_sqlConnectionBuilder, isReadOnly, null, logger, cancellationToken);
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

                    logger.LogInformation(ex, $"Attempt {retry}: {ex.Message}");
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
        /// <param name="applicationName">"Application name."</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        /// <exception>When executing this method, if exception is thrown that is not retriable or if last retry fails, then same exception is thrown by this method.</exception>
        public async Task ExecuteSql(SqlCommand sqlCommand, Func<SqlCommand, CancellationToken, Task> action, ILogger logger, string logMessage, CancellationToken cancellationToken, bool isReadOnly = false, bool disableRetries = false, string applicationName = null)
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
                    using SqlConnection sqlConnection = await _replicaHandler.GetConnection(_sqlConnectionBuilder, isReadOnly, applicationName, logger, cancellationToken);
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

        private static void InitReplicaHandler(CoreFeatureConfiguration coreFeatureConfiguration)
        {
            if (_replicaHandler == null) // this is needed, strictly speaking, only if SqlRetryService is not singleton, but it works either way.
            {
                lock (_initLocker)
                {
                    _replicaHandler ??= new ReplicaHandler(coreFeatureConfiguration);
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
            private CoreFeatureConfiguration _coreFeatureConfiguration;

            public ReplicaHandler(CoreFeatureConfiguration coreFeatureConfiguration)
            {
                _coreFeatureConfiguration = coreFeatureConfiguration;
            }

            public async Task<SqlConnection> GetConnection(ISqlConnectionBuilder sqlConnectionBuilder, bool isReadOnly, string applicationName, ILogger logger, CancellationToken cancel)
            {
                SqlConnection conn;
                var sw = Stopwatch.StartNew();
                var logSB = new StringBuilder("Long running retrieve SQL connection. ");
                var isReadOnlyConnection = isReadOnly ? "read-only " : string.Empty;

                if (!isReadOnly || !_coreFeatureConfiguration.SupportsSqlReplicas)
                {
                    logSB.AppendLine("Not read only. ");
                    conn = await sqlConnectionBuilder.GetSqlConnectionAsync(false, applicationName);
                }
                else
                {
                    logSB.AppendLine("Checking read only. ");
                    var replicaTrafficRatio = GetReplicaTrafficRatio(sqlConnectionBuilder, logger);
                    logSB.AppendLine($"Got replica traffic ratio in {sw.Elapsed.TotalSeconds} seconds. Ratio is {replicaTrafficRatio}. ");

                    if (replicaTrafficRatio < 0.5) // it does not make sense to use replica less than master at all
                    {
                        isReadOnlyConnection = string.Empty;
                        conn = await sqlConnectionBuilder.GetSqlConnectionAsync(false, applicationName);
                    }
                    else if (replicaTrafficRatio > 0.99)
                    {
                        conn = await sqlConnectionBuilder.GetSqlConnectionAsync(true, applicationName);
                    }
                    else
                    {
                        var useWriteConnection = unchecked(Interlocked.Increment(ref _usageCounter)) % (int)(1 / (1 - _replicaTrafficRatio)) == 1; // examples for ratio -> % divider = { 0.9 -> 10, 0.8 -> 5, 0.75 - 4, 0.67 - 3, 0.5 -> 2, <0.5 -> 1}
                        if (useWriteConnection)
                        {
                            isReadOnlyConnection = string.Empty;
                        }

                        conn = await sqlConnectionBuilder.GetSqlConnectionAsync(!useWriteConnection, applicationName);
                    }
                }

                // Connection is never opened by the _sqlConnectionBuilder but RetryLogicProvider is set to the old, deprecated retry implementation. According to the .NET spec, RetryLogicProvider
                // must be set before opening connection to take effect. Therefore we must reset it to null here before opening the connection.
                conn.RetryLogicProvider = null; // To remove this line _sqlConnectionBuilder in healthcare-shared-components must be modified.
                logger.LogDebug($"Retrieved {isReadOnlyConnection}connection to the database in {sw.Elapsed.TotalSeconds} seconds. Connection ID: {conn.ClientConnectionId}. ");
                if (sw.Elapsed.TotalSeconds > 1)
                {
                    logSB.AppendLine($"Retrieved {isReadOnlyConnection}connection to the database in {sw.Elapsed.TotalSeconds} seconds. Connection ID: {conn.ClientConnectionId}. ");
                    logger.LogWarning(logSB.ToString());
                }

                sw = Stopwatch.StartNew();
                await conn.OpenAsync(cancel);
                logger.LogDebug($"Opened {isReadOnlyConnection}connection to the database in {sw.Elapsed.TotalSeconds} seconds. Connection ID: {conn.ClientConnectionId}. ");
                if (sw.Elapsed.TotalSeconds > 1)
                {
                    logSB.AppendLine($"Opened {isReadOnlyConnection}connection to the database in {sw.Elapsed.TotalSeconds} seconds. Connection ID: {conn.ClientConnectionId}. ");
                    logger.LogWarning(logSB.ToString());
                }

                return conn;
            }

            private double GetReplicaTrafficRatio(ISqlConnectionBuilder sqlConnectionBuilder, ILogger logger)
            {
                const int trafficRatioCacheDurationSec = 600;
                if (_lastUpdated.HasValue && (DateTime.UtcNow - _lastUpdated.Value).TotalSeconds < trafficRatioCacheDurationSec)
                {
                    return _replicaTrafficRatio;
                }

                if (Monitor.TryEnter(_databaseAccessLocker, new TimeSpan(0, 0, 5)))
                {
                    try
                    {
                        if (_lastUpdated.HasValue && (DateTime.UtcNow - _lastUpdated.Value).TotalSeconds < trafficRatioCacheDurationSec)
                        {
                            logger.LogInformation("Waited, but used replica traffic cache");
                            return _replicaTrafficRatio;
                        }

                        logger.LogInformation("Updating replica traffic ratio");
                        _replicaTrafficRatio = GetReplicaTrafficRatioFromDatabase(sqlConnectionBuilder, logger);
                        _lastUpdated = DateTime.UtcNow;
                    }
                    finally
                    {
                        Monitor.Exit(_databaseAccessLocker);
                    }
                }
                else
                {
                    logger.LogInformation("Timed out waiting for replica traffic, using cached value");
                }

                return _replicaTrafficRatio;
            }

            private static double GetReplicaTrafficRatioFromDatabase(ISqlConnectionBuilder sqlConnectionBuilder, ILogger logger)
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
                catch (SqlException ex)
                {
                    logger.LogInformation(ex, "Failed to get replica traffic ratio from the database.");
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
