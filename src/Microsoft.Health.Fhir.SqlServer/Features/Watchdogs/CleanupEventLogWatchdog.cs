// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Data;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;

namespace Microsoft.Health.Fhir.SqlServer.Features.Watchdogs
{
    public class CleanupEventLogWatchdog : Watchdog<CleanupEventLogWatchdog>
    {
        private readonly ISqlRetryService _sqlRetryService;
        private readonly ILogger<CleanupEventLogWatchdog> _logger;
        private CancellationToken _cancellationToken;
        private const double _periodSec = 12 * 3600;
        private const double _leasePeriodSec = 3600;

        public CleanupEventLogWatchdog(ISqlRetryService sqlRetryService, ILogger<CleanupEventLogWatchdog> logger)
            : base(sqlRetryService, logger)
        {
            _sqlRetryService = EnsureArg.IsNotNull(sqlRetryService, nameof(sqlRetryService));
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
            using var cmd = new SqlCommand("dbo.CleanupEventLog") { CommandType = CommandType.StoredProcedure, CommandTimeout = 0 };
            await cmd.ExecuteNonQueryAsync(_sqlRetryService, _logger, _cancellationToken);
        }

        private async Task InitParamsAsync() // No CancellationToken is passed since we shouldn't cancel initialization.
        {
            _logger.LogInformation("InitParamsAsync starting...");

            using var cmd = new SqlCommand(@"
INSERT INTO dbo.Parameters (Id,Number) SELECT 'CleanupEventLog.DeleteBatchSize', 1000
INSERT INTO dbo.Parameters (Id,Number) SELECT 'CleanupEventLog.AllowedRows', 1e6
INSERT INTO dbo.Parameters (Id,Number) SELECT 'CleanupEventLog.RetentionPeriodDay', 30
INSERT INTO dbo.Parameters (Id,Number) SELECT 'CleanupEventLog.IsEnabled', 1
INSERT INTO dbo.Parameters (Id,Char) SELECT 'CleanpEventLog', 'LogEvent'
            ");
            await cmd.ExecuteNonQueryAsync(_sqlRetryService, _logger, CancellationToken.None, "InitParamsAsync failed.");

            _logger.LogInformation("InitParamsAsync completed.");
        }
    }
}
