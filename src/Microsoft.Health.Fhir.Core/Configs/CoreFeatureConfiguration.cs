// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search;

namespace Microsoft.Health.Fhir.Core.Configs
{
    /// <summary>
    /// Core feature configurations.
    /// </summary>
    public class CoreFeatureConfiguration
    {
        /// <summary>
        /// Defines CapabilityStatement.name
        /// </summary>
        public string SoftwareName { get; set; } = Resources.ServerName;

        /// <summary>
        /// Gets or sets a value indicating whether Batch is enabled or not.
        /// </summary>
        public bool SupportsBatch { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether Transaction is enabled or not.
        /// </summary>
        public bool SupportsTransaction { get; set; }

        /// <summary>
        /// Gets or sets the default value for IncludeTotal in search bundles.
        /// </summary>
        public TotalType IncludeTotalInBundle { get; set; } = TotalType.None;

        /// <summary>
        /// Gets or sets the maximum value for _count in search.
        /// </summary>
        public int MaxItemCountPerSearch { get; set; } = 1000;

        /// <summary>
        /// Gets or sets the default value for _count in search.
        /// </summary>
        public int DefaultItemCountPerSearch { get; set; } = 10;

        /// <summary>
        /// Gets or sets the max value for included search results.
        /// </summary>
        public int MaxIncludeCountPerSearch { get; set; } = 1000;

        /// <summary>
        /// Gets or sets the default value for included search results.
        /// </summary>
        public int DefaultIncludeCountPerSearch { get; set; } = 1000;

        /// <summary>
        /// Gets or sets a value whether we need to run profile validation during resource creation.
        /// </summary>
        public bool ProfileValidationOnCreate { get; set; } = false;

        /// <summary>
        /// Gets or sets a value whether we need to run profile validation during resource update.
        /// </summary>
        public bool ProfileValidationOnUpdate { get; set; } = false;

        /// <summary>
        /// Maximum items allowed to be deleted when using Conditional Delete.
        /// </summary>
        public int ConditionalDeleteMaxItems { get; set; } = 100;

        /// <summary>
        /// Gets or sets a value whether capturing resource change data is enabled or not.
        /// </summary>
        public bool SupportsResourceChangeCapture { get; set; } = false;

        /// <summary>
        /// Gets or sets the resource versioning policy.
        /// </summary>
        public VersioningConfiguration Versioning { get; set; } = new VersioningConfiguration();

        /// <summary>
        /// Gets or sets a value indicating whether the server supports the $status operation for SearchParameters.
        /// </summary>
        public bool SupportsSelectableSearchParameters { get; set; }

        /// <summary>
        /// Whether the service supports SQL read only replicas.
        /// </summary>
        public bool SupportsSqlReplicas { get; set; } = false;

        /// <summary>
        /// Gets or sets a value indicating whether the server supports the includes.
        /// </summary>
        public bool SupportsIncludes { get; set; } = false;

        /// <summary>
        /// Gets or sets a value indicating whether geo-redundancy monitoring is enabled.
        /// When enabled, the system will monitor geo-replication lag and status through
        /// the GeoReplicationLagWatchdog. This feature is only applicable when using
        /// Azure SQL Database with geo-replication configured.
        /// </summary>
        public bool EnableGeoRedundancy { get; set; }

        /// <summary>
        /// Gets or sets the refresh interval in seconds for the SearchParameter cache background service.
        /// The background service will call EnsureCacheFreshnessAsync at this interval to keep
        /// SearchParameter cache synchronized across instances. Default is 60 seconds if not specified.
        /// </summary>
        public int SearchParameterCacheRefreshIntervalSeconds { get; set; } = 60;
    }
}
