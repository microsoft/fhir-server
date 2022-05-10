// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Operations.Import.Models;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    /// <summary>
    /// Import task input payload
    /// </summary>
    public class ImportOrchestratorTaskInputData
    {
        public int TypeId { get; set; }

        /// <summary>
        /// Request Uri for the import operation
        /// </summary>
        public Uri RequestUri { get; set; }

        /// <summary>
        /// Input format for the input resource: ndjson supported.
        /// </summary>
        public string InputFormat { get; set; }

        /// <summary>
        /// Input sourece for the operation.
        /// </summary>
        public Uri InputSource { get; set; }

        /// <summary>
        /// FHIR Base Uri
        /// </summary>
        public Uri BaseUri { get; set; }

        /// <summary>
        /// Input resource list
        /// </summary>
        public IReadOnlyList<InputResource> Input { get; set; }

        /// <summary>
        /// Resource storage details.
        /// </summary>
        public ImportRequestStorageDetail StorageDetail { get; set; }

        /// <summary>
        /// Task create time.
        /// </summary>
        public DateTimeOffset TaskCreateTime { get; set; }

        /// <summary>
        /// Start sequence id
        /// </summary>
        public long StartSequenceId { get; set; }
    }
}
