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
        /// Gets or sets the default value for maximum value for _count in search.
        /// </summary>
        public int MaxItemCountPerSearch { get; set; } = 1000;

        /// <summary>
        /// Gets or sets the default value for _count in search.
        /// </summary>
        public int DefaultItemCountPerSearch { get; set; } = 10;

        /// <summary>
        /// Gets or sets the default value for included search results.
        /// </summary>
        public int DefaultIncludeCountPerSearch { get; set; } = 100;

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
    }
}
