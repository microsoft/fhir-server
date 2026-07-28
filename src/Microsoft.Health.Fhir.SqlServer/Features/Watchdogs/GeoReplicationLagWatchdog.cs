// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Medino;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Metrics;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.SqlServer.Features.Schema;
using Microsoft.Health.SqlServer.Features.Storage;

namespace Microsoft.Health.Fhir.SqlServer.Features.Watchdogs
{
    /// <summary>
    /// Monitors Azure SQL Database geo-replication lag and status for FHIR databases.
    /// This watchdog periodically checks the replication status between primary and secondary replicas
    /// in an Azure SQL Database geo-replication setup, logging warnings and errors when issues are detected.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The GeoReplicationLagWatchdog is designed to work specifically with Azure SQL Database instances
    /// that have geo-replication configured. It queries the sys.dm_geo_replication_link_status dynamic
    /// management view to retrieve replication metrics.
    /// </para>
    /// </remarks>
    internal sealed class GeoReplicationLagWatchdog : Watchdog<GeoReplicationLagWatchdog>
    {
        private readonly ILogger<GeoReplicationLagWatchdog> _logger;
        private readonly ISqlRetryService _sqlRetryService;
        private readonly IMediator _mediator;
        private readonly SchemaInformation _schemaInformation;

        private const string CatchUpReplicationLagDescription = "CATCH_UP";
        private const string SeedingUpReplicationLagDescription = "SEEDING";
        private const string PendingReplicationLagDescription = "PENDING";

        private const int MinRequiredSchemaVersion = (int)SchemaVersion.V92;

        public GeoReplicationLagWatchdog(
            ISqlRetryService sqlRetryService,
            ILogger<GeoReplicationLagWatchdog> logger,
            IMediator mediator,
            SchemaInformation schemaInformation)
            : base(sqlRetryService, logger)
        {
            _sqlRetryService = EnsureArg.IsNotNull(sqlRetryService, nameof(sqlRetryService));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
            _mediator = EnsureArg.IsNotNull(mediator, nameof(mediator));
            _schemaInformation = EnsureArg.IsNotNull(schemaInformation, nameof(schemaInformation));
        }

        internal GeoReplicationLagWatchdog()
        {
            // this is used to get param names for testing
        }

        public override double LeasePeriodSec { get; internal set; } = 120;

        public override bool AllowRebalance { get; internal set; } = true;

        public override double PeriodSec { get; internal set; } = 30;

        protected override async Task RunWorkAsync(CancellationToken cancellationToken)
        {
            // Check if the current schema version supports the GetGeoReplicationLag stored procedure
            if (_schemaInformation.Current < MinRequiredSchemaVersion)
            {
                _logger.LogDebug(
                    "GeoReplicationLagWatchdog: Current schema version {CurrentVersion} is below minimum required version {MinRequiredVersion}. Skipping execution.",
                    _schemaInformation.Current,
                    MinRequiredSchemaVersion);
                return;
            }

            await using var cmd = new SqlCommand("dbo.GetGeoReplicationLag") { CommandType = CommandType.StoredProcedure };

            try
            {
                var replicationInfos = await _sqlRetryService.ExecuteReaderAsync(
                    cmd,
                    reader => new
                    {
                        ReplicationState = reader.IsDBNull("replication_state_desc") ? null : reader.GetString("replication_state_desc", 0),
                        LagSeconds = reader.IsDBNull("replication_lag_sec") ? (int?)null : reader.GetInt32("replication_lag_sec", 1),
                        LastReplication = reader.IsDBNull("last_replication") ? (DateTimeOffset?)null : reader.GetDateTimeOffset("last_replication", 2),
                    },
                    _logger,
                    "Failed to get geo-replication lag information",
                    cancellationToken,
                    isReadOnly: true);

                if (replicationInfos.Count == 0)
                {
                    _logger.LogDebug("GeoReplicationLagWatchdog: No geo-replication configured or database is not primary.");
                    return;
                }

                foreach (var info in replicationInfos)
                {
                    // Log warning if replication lag is high (e.g., > 300 seconds)
                    if (info.LagSeconds.HasValue)
                    {
                        if (info.LagSeconds.HasValue && info.LagSeconds.Value > 300)
                        {
                            _logger.LogWarning("GeoReplicationLagWatchdog: High replication lag detected: {LagSeconds} seconds", info.LagSeconds.Value);
                        }
                        else
                        {
                            _logger.LogDebug("GeoReplicationLagWatchdog: Replication state={ReplicationState}, lag={LagSeconds}s, last replication={LastReplication}", info.ReplicationState, info.LagSeconds, info.LastReplication);
                        }
                    }

                    await _mediator.PublishAsync(
                        new GeoReplicationLagNotification
                        {
                            ReplicationState = info.ReplicationState,
                            LagSeconds = info.LagSeconds,
                            LastReplication = info.LastReplication,
                        },
                        cancellationToken);

                    // Log error if replication state indicates issues
                    if (!string.IsNullOrEmpty(info.ReplicationState) &&
                        !info.ReplicationState.Equals(CatchUpReplicationLagDescription, StringComparison.OrdinalIgnoreCase) &&
                        !info.ReplicationState.Equals(SeedingUpReplicationLagDescription, StringComparison.OrdinalIgnoreCase) &&
                        !info.ReplicationState.Equals(PendingReplicationLagDescription, StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogError("GeoReplicationLagWatchdog: Replication state issue: {ReplicationState}", info.ReplicationState);
                        await _sqlRetryService.TryLogEvent("GeoReplicationLagWatchdog", "Error", $"Replication state: {info.ReplicationState}", null, cancellationToken);
                    }
                }
            }
            catch (SqlException ex) when (ex.Number == 208)
            {
                // Handle the case where geo-replication view doesn't exist (non-Azure SQL Database)
                _logger.LogDebug("GeoReplicationLagWatchdog: Geo-replication monitoring not available - not running on Azure SQL Database with geo-replication enabled");
                return;
            }
        }
    }
}
