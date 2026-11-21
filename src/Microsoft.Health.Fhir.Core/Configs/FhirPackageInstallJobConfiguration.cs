// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Operations;

namespace Microsoft.Health.Fhir.Core.Configs
{
    /// <summary>
    /// Controls the SQL FHIR package installation queue and cache convergence wait.
    /// </summary>
    public class FhirPackageInstallJobConfiguration : HostingBackgroundServiceQueueItem
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FhirPackageInstallJobConfiguration"/> class.
        /// </summary>
        public FhirPackageInstallJobConfiguration()
        {
            Queue = QueueType.FhirPackageInstall;
        }

        /// <summary>
        /// Gets or sets the maximum wait as a multiple of the SearchParameter cache refresh interval.
        /// </summary>
        public int CacheUpdateMaxWaitMultiplier { get; set; } = 40;

        /// <summary>
        /// Gets or sets the active-host lookback as a multiple of the SearchParameter cache refresh interval.
        /// </summary>
        public int ActiveHostsEventsMultiplier { get; set; } = 9;
    }
}
