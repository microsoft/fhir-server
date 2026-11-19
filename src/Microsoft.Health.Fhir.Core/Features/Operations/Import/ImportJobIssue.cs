// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    /// <summary>
    /// A warning about imported resources of a type, returned in the import status.
    /// </summary>
    public class ImportJobIssue
    {
        /// <summary>
        /// Gets or sets the severity of the issue, as a FHIR issue severity code (for example <c>information</c>).
        /// </summary>
        [JsonProperty("severity")]
        public string Severity { get; set; }

        /// <summary>
        /// Gets or sets the resource type.
        /// </summary>
        [JsonProperty("type")]
        public string Type { get; set; }

        /// <summary>
        /// Gets or sets the number of affected resources.
        /// </summary>
        [JsonProperty("count")]
        public long Count { get; set; }

        /// <summary>
        /// Gets or sets the description of the issue.
        /// </summary>
        [JsonProperty("diagnostics")]
        public string Diagnostics { get; set; }
    }
}
