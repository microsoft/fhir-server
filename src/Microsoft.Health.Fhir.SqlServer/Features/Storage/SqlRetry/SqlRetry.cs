// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#pragma warning disable SA1649 // File name should match first type name. TODO: split this file so each class has it's own.
#pragma warning disable SA1402 // File may only contain a single type

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using Microsoft.Health.SqlServer;
using Microsoft.Health.SqlServer.Configs;
using static Microsoft.Health.Fhir.SqlServer.Features.Storage.SqlRetryService;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage // TODO: namespace in fhir-shared?
{
    public interface ISqlRetryService
    {
        Task ExecuteSqlWithRetries(SqlCommand sqlCommand, SqlConnectionInitializer sqlConnectionInitializer, ExecuteSqlWithRetriesAction action, CancellationToken cancellationToken);

        Task<IList<T>> ExecuteSqlReaderWithRetries<T>(SqlCommand sqlCommand, Func<SqlDataReader, T> toT, CancellationToken cancellationToken);

        Task<T> ProcessSqlDataReaderRowsWithRetries<T>(SqlCommand sqlCommand, SqlConnectionInitializer sqlConnectionInitializer, InitializeRowProcessor<T> initializer, ExecuteRowProcessor<T> processRow, CancellationToken cancellationToken);
    }

    public class SqlRetryServiceOptions
    {
        public const string SqlServer = "SqlServer";

        public int MaxRetries { get; set; } = 5;

        public int RetryMillisecondsDelay { get; set; } = 5000;

#pragma warning disable CA1002 // Do not expose generic lists
        public List<int> RemoveTransientErrors { get; } = new List<int>();
#pragma warning restore CA1002 // Do not expose generic lists

#pragma warning disable CA1002 // Do not expose generic lists
        public List<int> AddTransientErrors { get; } = new List<int>();
#pragma warning restore CA1002 // Do not expose generic lists

        /*
        #pragma warning disable CA2227 // Collection properties should be read only
                public HashSet<int> RemoveTransientErrors { get; set; }
        #pragma warning restore CA2227 // Collection properties should be read only

        #pragma warning disable CA2227 // Collection properties should be read only
                public HashSet<int> AddTransientErrors { get; set; }
        #pragma warning restore CA2227 // Collection properties should be read only*/
    }

    public class SqlRetryServiceDelegateOptions
    {
        public bool DefaultIsExceptionRetriableOff { get; init; }

        public IsExceptionRetriable CustomIsExceptionRetriable { get; init; }
    }

    public class SqlRetryService : ISqlRetryService
    {
        private readonly ISqlConnectionBuilder _sqlConnectionBuilder;
        private readonly SqlServerDataStoreConfiguration _sqlServerDataStoreConfiguration;

        // Default errors copied from src/Microsoft.Data.SqlClient/src/Microsoft/Data/SqlClient/Reliability/SqlConfigurableRetryFactory.cs .
        private readonly HashSet<int> transientErrors
            = new HashSet<int>
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

        private readonly IsExceptionRetriable defaultIsExceptionRetriable = DefaultIsExceptionRetriable;
        private readonly bool defaultIsExceptionRetriableOff;
        private readonly IsExceptionRetriable customIsExceptionRetriable;
        private readonly int maxRetries;
        private readonly int retryMillisecondsDelay;

        public SqlRetryService(
            ISqlConnectionBuilder sqlConnectionBuilder,
            IOptions<SqlServerDataStoreConfiguration> sqlServerDataStoreConfiguration,
            IOptions<SqlRetryServiceOptions> sqlRetryServiceOptions,
            SqlRetryServiceDelegateOptions sqlRetryServiceDelegateOptions)
        {
            EnsureArg.IsNotNull(sqlConnectionBuilder, nameof(sqlConnectionBuilder));
            _sqlServerDataStoreConfiguration = EnsureArg.IsNotNull(sqlServerDataStoreConfiguration?.Value, nameof(sqlServerDataStoreConfiguration));
            _sqlConnectionBuilder = sqlConnectionBuilder;

            EnsureArg.IsNotNull(sqlRetryServiceOptions?.Value, nameof(sqlRetryServiceOptions));
            if (sqlRetryServiceOptions.Value.RemoveTransientErrors != null)
            {
                transientErrors.ExceptWith(sqlRetryServiceOptions.Value.RemoveTransientErrors);
            }

            if (sqlRetryServiceOptions.Value.AddTransientErrors != null)
            {
                transientErrors.UnionWith(sqlRetryServiceOptions.Value.AddTransientErrors);
            }

            maxRetries = sqlRetryServiceOptions.Value.MaxRetries;
            retryMillisecondsDelay = sqlRetryServiceOptions.Value.RetryMillisecondsDelay;

            EnsureArg.IsNotNull(sqlRetryServiceDelegateOptions, nameof(sqlRetryServiceDelegateOptions));
            defaultIsExceptionRetriableOff = sqlRetryServiceDelegateOptions.DefaultIsExceptionRetriableOff;
            customIsExceptionRetriable = sqlRetryServiceDelegateOptions.CustomIsExceptionRetriable;
        }

        public delegate bool IsExceptionRetriable(Exception ex);

        public delegate Task ExecuteSqlWithRetriesAction(SqlCommand sqlCommand, CancellationToken cancellationToken);

        public delegate Task<bool> ExecuteRowProcessor<T>(SqlDataReader reader, T result, CancellationToken cancellationToken);

        public delegate T InitializeRowProcessor<T>();

        public delegate void SqlConnectionInitializer(SqlConnection sqlConnection);

        // TODO also define toT action delegate.

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
            if (ex is SqlException sqlEx && transientErrors.Contains(sqlEx.Number))
            {
                return true;
            }

            if (!defaultIsExceptionRetriableOff && defaultIsExceptionRetriable(ex))
            {
                return true;
            }

            if (customIsExceptionRetriable(ex))
            {
                return true;
            }

            if (ex.IsRetryable())
            {
                return true;
            }

            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public async Task ExecuteSqlWithRetries(SqlCommand sqlCommand, SqlConnectionInitializer sqlConnectionInitializer, ExecuteSqlWithRetriesAction action, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(sqlCommand, nameof(sqlCommand));
            EnsureArg.IsNotNull(action, nameof(action));

            sqlCommand.CommandTimeout = (int)_sqlServerDataStoreConfiguration.CommandTimeout.TotalSeconds;

            int retry = 0;
            while (true)
            {
                try
                {
                    using SqlConnection connection = await _sqlConnectionBuilder.GetSqlConnectionAsync(initialCatalog: null, cancellationToken: cancellationToken).ConfigureAwait(false); // TODO: change GetSqlConnection, this still uses old retry? Also, set connection timeout.
                    sqlConnectionInitializer?.Invoke(connection);
                    await connection.OpenAsync(cancellationToken);
                    sqlCommand.Connection = connection;
                    await action(sqlCommand, cancellationToken);
                    return;
                }
                catch (Exception ex)
                {
                    if (!RetryTest(ex) || ++retry >= maxRetries)
                    {
                        throw;
                    }
                }

                await Task.Delay(retryMillisecondsDelay, cancellationToken);
            }
        }

        /*public async Task<IList<T>> ExecuteSqlReaderWithRetries<T>(SqlCommand sqlCommand, Func<SqlDataReader, T> toT, CancellationToken cancellationToken)
        {
            IList<T> results = null;
            await ExecuteSqlWithRetries(
                sqlCommand,
                null,
                async (sqlCommandInt, cancellationToken) =>
                {
                    using SqlDataReader reader = await sqlCommandInt.ExecuteReaderAsync(cancellationToken);
                    results = new List<T>();
                    while (await reader.ReadAsync(cancellationToken))
                    {
                        results.Add(toT(reader));
                    }

                    await reader.NextResultAsync(cancellationToken);
                },
                cancellationToken);

            return results;
        }*/

        public async Task<IList<T>> ExecuteSqlReaderWithRetries<T>(SqlCommand sqlCommand, Func<SqlDataReader, T> toT, CancellationToken cancellationToken)
        {
            return await ProcessSqlDataReaderRowsWithRetries<IList<T>>(
                sqlCommand,
                null,
                () => new List<T>(),
                (reader, result, cancellationToken) =>
                {
                    result.Add(toT(reader));
                    return Task.FromResult(true);
                },
                cancellationToken);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public async Task<T> ProcessSqlDataReaderRowsWithRetries<T>(SqlCommand sqlCommand, SqlConnectionInitializer sqlConnectionInitializer, InitializeRowProcessor<T> initializer, ExecuteRowProcessor<T> processRow, CancellationToken cancellationToken)
        {
            var results = default(T);
            await ExecuteSqlWithRetries(
                sqlCommand,
                sqlConnectionInitializer,
                async (sqlCommandInt, cancellationToken) =>
                {
                    using SqlDataReader reader = await sqlCommandInt.ExecuteReaderAsync(cancellationToken);
                    results = initializer();
                    while (await reader.ReadAsync(cancellationToken) && await processRow(reader, results, cancellationToken))
                    {
                    }

                    await reader.NextResultAsync(cancellationToken);
                },
                cancellationToken);

            return results;
        }
    }
}
