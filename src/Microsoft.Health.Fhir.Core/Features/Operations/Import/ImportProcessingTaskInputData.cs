﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    public class ImportProcessingTaskInputData
    {
        /// <summary>
        /// Resource location for the input file
        /// </summary>
        public string ResourceLocation { get; set; }

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
        /// Data processing task id
        /// </summary>
        public string TaskId { get; set; }

        /// <summary>
        /// Begine sequence id
        /// </summary>
        public long BeginSequenceId { get; set; }

        /// <summary>
        /// End sequence id
        /// </summary>
        public long EndSequenceId { get; set; }
    }
}
