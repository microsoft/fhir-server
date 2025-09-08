// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import.Models
{
    public class ImportRequest
    {
        /// <summary>
        /// Determines the format of the the input data.
        /// </summary>
        public string InputFormat { get; set; }

        /// <summary>
        /// Determines the location of the source.
        /// Should be a uri pointing to the source.
        /// </summary>
        public Uri InputSource { get; set; }

        /// <summary>
        /// Determines the details of the input file that should be imported containing in the input source.
        /// </summary>
        public IReadOnlyList<InputResource> Input { get; set; }

        /// <summary>
        /// Determines the details of the storage.
        /// </summary>
        public ImportRequestStorageDetail StorageDetail { get; set; }

        /// <summary>
        /// Import operation mode
        /// </summary>
        public string Mode { get; set; }

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
        /// Force import, ignore server status and import mode check
        /// </summary>
        public bool Force { get; set; }

        /// <summary>
        /// Custom container name for error logs. If not specified, the default container will be used.
        /// </summary>
        public string ErrorContainerName { get; set; }

        /// <summary>
        /// Number of bytes to be read by processing job.
        /// </summary>
        public int ProcessingUnitBytesToRead { get; set; }
    }
}
