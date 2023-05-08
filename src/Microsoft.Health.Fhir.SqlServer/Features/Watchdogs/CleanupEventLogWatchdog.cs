// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Data;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.SqlServer.Features.Client;

namespace Microsoft.Health.Fhir.SqlServer.Features.Watchdogs
{
    public class CleanupEventLogWatchdog : Watchdog<CleanupEventLogWatchdog>
    {
        private readonly IBackgroundScopeProvider<SqlConnectionWrapperFactory> _sqlConnectionWrapperFactory;
        private readonly ILogger<CleanupEventLogWatchdog> _logger;
        private CancellationToken _cancellationToken;
        private const double _periodSec = 12 * 3600;
        private const double _leasePeriodSec = 3600;

        public CleanupEventLogWatchdog(IBackgroundScopeProvider<SqlConnectionWrapperFactory> sqlConnectionWrapperFactory, ILogger<CleanupEventLogWatchdog> logger)
            : base(sqlConnectionWrapperFactory, logger)
        {
            _sqlConnectionWrapperFactory = EnsureArg.IsNotNull(sqlConnectionWrapperFactory, nameof(sqlConnectionWrapperFactory));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
        }

        internal CleanupEventLogWatchdog()
            : base()
        {
            // this is used to get param names for testing
        }

        internal async Task StartAsync(CancellationToken cancellationToken)
        {
            _cancellationToken = cancellationToken;
            await InitParamsAsync();
            await StartAsync(true, _periodSec, _leasePeriodSec, cancellationToken);
        }

        protected override async Task ExecuteAsync()
        {
            using IScoped<SqlConnectionWrapperFactory> scopedConn = _sqlConnectionWrapperFactory.Invoke();
            using SqlConnectionWrapper conn = await scopedConn.Value.ObtainSqlConnectionWrapperAsync(CancellationToken.None, false);
            using SqlCommandWrapper cmd = conn.CreateRetrySqlCommand();
            cmd.CommandText = "dbo.CleanupEventLog";
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandTimeout = 0;
            await cmd.ExecuteNonQueryAsync(_cancellationToken);
        }

        private async Task InitParamsAsync() // No CancellationToken is passed since we shouldn't cancel initialization.
        {
            _logger.LogInformation("InitParamsAsync starting...");

            using IScoped<SqlConnectionWrapperFactory> scopedConn = _sqlConnectionWrapperFactory.Invoke();
            using SqlConnectionWrapper conn = await scopedConn.Value.ObtainSqlConnectionWrapperAsync(CancellationToken.None, false);
            using SqlCommandWrapper cmd = conn.CreateRetrySqlCommand();
            cmd.CommandText = @"
INSERT INTO dbo.Parameters (Id,Number) SELECT 'CleanupEventLog.DeleteBatchSize', 1000
INSERT INTO dbo.Parameters (Id,Number) SELECT 'CleanupEventLog.AllowedRows', 1e6
INSERT INTO dbo.Parameters (Id,Number) SELECT 'CleanupEventLog.RetentionPeriodDay', 30
INSERT INTO dbo.Parameters (Id,Number) SELECT 'CleanupEventLog.IsEnabled', 1
INSERT INTO dbo.Parameters (Id,Char) SELECT 'CleanpEventLog', 'LogEvent'
            ";
            await cmd.ExecuteNonQueryAsync(CancellationToken.None);

            _logger.LogInformation("InitParamsAsync completed.");
        }
    }
}
