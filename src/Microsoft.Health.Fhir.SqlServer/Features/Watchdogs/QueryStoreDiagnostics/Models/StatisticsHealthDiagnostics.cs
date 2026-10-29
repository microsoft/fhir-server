// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.SqlServer.Features.Watchdogs.QueryStoreDiagnostics.Models
{
    /// <summary>
    /// Table statistics health for one statistics object. Unlike the slow-query and query-plan payloads, instances of
    /// this type are serialized to JSON and carried several to a log line: the rows are small, uniform, and contain no
    /// free text, so a batch of them has a predictable size and nothing is lost by giving up per-field columns.
    /// Properties are public because <see cref="System.Text.Json.JsonSerializer"/> only serializes public members;
    /// the type itself is internal because nothing outside this assembly consumes it.
    /// </summary>
    internal sealed class StatisticsHealthDiagnostics
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
        /// Gets or sets the timestamp when the diagnostics were collected. Carried on the row rather than only on the
        /// log line so that a row stays self-describing once it is lifted out of the batch it was emitted in.
        /// </summary>
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    }
}
