// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Models
{
    public class BulkImportRequestStorageDetailConfiguration
    {
        /// <summary>
        /// Determines the types of the storage
        /// </summary>
        public string Type { get; set; } = "https";

        /// <summary>
        /// Determines the parameters of the storage depending on type
        /// </summary>
        public object Parameters { get; set; }
    }
}
