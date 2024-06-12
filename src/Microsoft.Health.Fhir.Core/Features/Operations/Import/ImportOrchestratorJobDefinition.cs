﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Operations.Import.Models;
using Microsoft.Health.JobManagement;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    /// <summary>
    /// Import job input payload
    /// </summary>
    public class ImportOrchestratorJobDefinition : IJobData
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
    }
}
