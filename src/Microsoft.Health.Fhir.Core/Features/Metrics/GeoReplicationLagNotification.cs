// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.Core.Features.Metrics
{
    /// <summary>
    /// A Medino message containing information about geo-replication lag.
    /// This gets emitted by the GeoReplicationLagWatchdog.
    /// Consume these using Medino to collect stats about geo-replication health.
    /// </summary>
    public class GeoReplicationLagNotification : IMetricsNotification
    {
        /// <summary>
        /// The current replication state description.
        /// </summary>
        public string ReplicationState { get; set; }

        /// <summary>
        /// The number of seconds the secondary is behind the primary.
        /// </summary>
        public int? LagSeconds { get; set; }

        /// <summary>
        /// The timestamp when the secondary last hardened a log block.
        /// </summary>
        public DateTimeOffset? LastReplication { get; set; }

        /// <summary>
        /// Timestamp when the notification was created.
        /// </summary>
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

        /// <summary>
        /// The FHIR operation associated with this notification.
        /// For geo-replication monitoring, this represents the infrastructure operation.
        /// </summary>
        public string FhirOperation => "geo-replication-monitoring";

        /// <summary>
        /// The resource type associated with this notification.
        /// For geo-replication monitoring, this is not resource-specific.
        /// </summary>
        public string ResourceType => "System";
    }
}
