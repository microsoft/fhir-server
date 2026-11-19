// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.JobManagement;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.FhirPackages
{
    /// <summary>
    /// Identifies an exact package installation in the durable job queue.
    /// </summary>
    public sealed class FhirPackageInstallJobDefinition : IJobData
    {
        /// <summary>
        /// Gets the job type.
        /// </summary>
        [JsonProperty("typeId", Order = 0)]
        public int TypeId => (int)JobType.FhirPackageInstall;

        /// <summary>
        /// Gets or sets the exact npm package name.
        /// </summary>
        [JsonProperty(Order = 1)]
        public string PackageId { get; set; }

        /// <summary>
        /// Gets or sets the exact package version.
        /// </summary>
        [JsonProperty(Order = 2)]
        public string Version { get; set; }

        /// <summary>
        /// Gets or sets the deployment-configured source identifier.
        /// </summary>
        [JsonProperty(Order = 3)]
        public string Source { get; set; }

        /// <summary>
        /// Gets or sets the unique request identifier, which keeps each enqueued definition distinct.
        /// </summary>
        [JsonProperty(Order = 4)]
        public string Id { get; set; }
    }
}
