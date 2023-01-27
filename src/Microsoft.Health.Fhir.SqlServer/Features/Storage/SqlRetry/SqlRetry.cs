// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#pragma warning disable SA1649 // File name should match first type name. TODO: split this file so each class has it's own.
#pragma warning disable SA1402 // File may only contain a single type

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using static Microsoft.Health.Fhir.SqlServer.Features.Storage.SqlRetryService;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage // TODO: namespace in health-shared?
{
    public interface ISqlRetryService
    {
        Task ExecuteWithRetries(RetriableAction action);
    }

    public class SqlRetryServiceOptions
    {
        public const string SqlServer = "SqlServer";

        public int MaxRetries { get; set; } = 5;

        public int RetryMillisecondsDelay { get; set; } = 5000;

        public IList<int> RemoveTransientErrors { get; } = new List<int>();

        public IList<int> AddTransientErrors { get; } = new List<int>();
    }

    public class SqlRetryServiceDelegateOptions
    {
        public bool DefaultIsExceptionRetriableOff { get; init; }

        public IsExceptionRetriable CustomIsExceptionRetriable { get; init; }
    }

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

        private readonly IsExceptionRetriable _defaultIsExceptionRetriable = DefaultIsExceptionRetriable;
        private readonly bool _defaultIsExceptionRetriableOff;
        private readonly IsExceptionRetriable _customIsExceptionRetriable;
        private readonly int _maxRetries;
        private readonly int _retryMillisecondsDelay;

        public SqlRetryService(
            IOptions<SqlRetryServiceOptions> sqlRetryServiceOptions,
            SqlRetryServiceDelegateOptions sqlRetryServiceDelegateOptions)
        {
            EnsureArg.IsNotNull(sqlRetryServiceOptions?.Value, nameof(sqlRetryServiceOptions));
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

            EnsureArg.IsNotNull(sqlRetryServiceDelegateOptions, nameof(sqlRetryServiceDelegateOptions));
            _defaultIsExceptionRetriableOff = sqlRetryServiceDelegateOptions.DefaultIsExceptionRetriableOff;
            _customIsExceptionRetriable = sqlRetryServiceDelegateOptions.CustomIsExceptionRetriable;
        }

        public delegate bool IsExceptionRetriable(Exception ex);

        public delegate Task RetriableAction();

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

        public async Task ExecuteWithRetries(RetriableAction action)
        {
            EnsureArg.IsNotNull(action, nameof(action));

            int retry = 0;
            while (true)
            {
                try
                {
                    await action();
                    return;
                }
                catch (Exception ex)
                {
                    if (!RetryTest(ex) || ++retry >= _maxRetries)
                    {
                        throw;
                    }
                }

                await Task.Delay(_retryMillisecondsDelay);
            }
        }
    }
}
