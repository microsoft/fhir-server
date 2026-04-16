// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.JobManagement;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    public class ImportProcessingJobDefinition : IJobData
    {
        public int TypeId { get; set; }

        /// <summary>
        /// Resource location for the input file
        /// </summary>
        public string ResourceLocation { get; set; }

        /// <summary>
        /// Offset to read input blob/file from
        /// </summary>
        public long Offset { get; set; }

        /// <summary>
        /// Number of bytes to read
        /// </summary>
        public int BytesToRead { get; set; }

        /// <summary>
        /// Request Uri string for the import operation
        /// </summary>
#pragma warning disable CA1056
        public string UriString { get; set; }
#pragma warning restore CA1056

        /// <summary>
        /// FHIR base uri string.
        /// </summary>
#pragma warning disable CA1056
        public string BaseUriString { get; set; }
#pragma warning restore CA1056

        /// <summary>
        /// FHIR resource type
        /// </summary>
        public string ResourceType { get; set; }

        /// <summary>
        /// Group id
        /// </summary>
        public long GroupId { get; set; }

        /// <summary>
        /// Import mode.
        /// </summary>
        public ImportMode ImportMode { get; set; }

        /// <summary>
        /// Flag indicating how late arivals are handled.
        /// Late arrival is a resource with explicit last updated and no explicit version. Its last updated is less than last updated on current version in the database.
        /// If late arrival conflicts with exting resource versions in the database, it is currently marked as a conflict and not ingested.
        /// With this flag set to true, it can be ingested with negative version value.
        /// </summary>
        public bool AllowNegativeVersions { get; set; }

        /// <summary>
        /// Flag indicating whether FHIR index updates are handled in the same SQL transaction with Resource inserts.
        /// Flag is relevant only for resource creates. Default value is false.
        /// </summary>
        public bool EventualConsistency { get; set; }

        /// <summary>
        /// Custom container name for error logs. If not specified, the default container will be used.
        /// </summary>
        public string ErrorContainerName { get; set; }
    }
}
