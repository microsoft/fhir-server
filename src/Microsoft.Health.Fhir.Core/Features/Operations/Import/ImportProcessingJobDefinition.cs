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
    }
}
