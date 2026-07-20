// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Operations.Security
{
    /// <summary>
    /// Represents the result of authorizing an export job creation request.
    /// </summary>
    public sealed class ExportCreateAuthorizationResult
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ExportCreateAuthorizationResult"/> class.
        /// </summary>
        /// <param name="resourceTypeToPersist">The canonical comma-separated resource types to persist on the export job.</param>
        public ExportCreateAuthorizationResult(string resourceTypeToPersist)
        {
            ResourceTypeToPersist = resourceTypeToPersist;
        }

        /// <summary>
        /// Gets the canonical comma-separated resource types to persist on the export job, or <c>null</c> when the job is unconstrained.
        /// </summary>
        public string ResourceTypeToPersist { get; }
    }
}
