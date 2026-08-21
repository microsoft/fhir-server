// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.Core.Features.Metrics
{
    /// <summary>
    /// Contains table statistics health information.
    /// </summary>
    public class StatisticsHealthNotification : IMetricsNotification
    {
        /// <summary>
        /// Gets or sets the schema that owns the table.
        /// </summary>
        public string SchemaName { get; set; }

        /// <summary>
        /// Gets or sets the table name.
        /// </summary>
        public string TableName { get; set; }

        /// <summary>
        /// Gets or sets the statistics object name.
        /// </summary>
        public string StatisticsName { get; set; }

        /// <summary>
        /// Gets or sets the timestamp when the statistics were last updated.
        /// </summary>
        public DateTimeOffset? LastUpdated { get; set; }

        /// <summary>
        /// Gets or sets the number of rows represented by the statistics.
        /// </summary>
        public long? Rows { get; set; }

        /// <summary>
        /// Gets or sets the number of rows sampled to build the statistics.
        /// </summary>
        public long? RowsSampled { get; set; }

        /// <summary>
        /// Gets or sets the number of modifications since the statistics were last updated.
        /// </summary>
        public long? ModificationCounter { get; set; }

        /// <summary>
        /// Gets or sets the percentage of represented rows modified since the statistics were last updated.
        /// </summary>
        public double? ModificationPercent { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether SQL Server automatically created the statistics.
        /// </summary>
        public bool IsAutoCreated { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether a user created the statistics.
        /// </summary>
        public bool IsUserCreated { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the statistics are associated with an index.
        /// </summary>
        public bool IsFromIndex { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the statistics use a filter.
        /// </summary>
        public bool HasFilter { get; set; }

        /// <summary>
        /// Gets or sets the timestamp when the notification was created.
        /// </summary>
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

        /// <summary>
        /// Gets the FHIR operation associated with this notification.
        /// </summary>
        public string FhirOperation => "query-store-diagnostics";

        /// <summary>
        /// Gets the resource type associated with this notification.
        /// </summary>
        public string ResourceType => "System";
    }
}
