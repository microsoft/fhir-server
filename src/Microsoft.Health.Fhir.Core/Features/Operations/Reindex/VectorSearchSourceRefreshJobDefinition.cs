// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.JobManagement;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Reindex
{
    /// <summary>
    /// Describes a source-resource change that requires dependent vector search indices to be refreshed.
    /// </summary>
    public class VectorSearchSourceRefreshJobDefinition : IJobData
    {
        /// <summary>
        /// Gets or sets the job type identifier.
        /// </summary>
        public int TypeId { get; set; }

        /// <summary>
        /// Gets or sets the source resource type.
        /// </summary>
        public string SourceResourceType { get; set; }

        /// <summary>
        /// Gets or sets the source resource identifier.
        /// </summary>
        public string SourceResourceId { get; set; }

        /// <summary>
        /// Gets or sets the source resource version that caused the refresh.
        /// </summary>
        public string SourceResourceVersion { get; set; }
    }
}
