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
        /// Force import, ignore server status and import mode check
        /// </summary>
        public bool Force { get; set; }
    }
}
