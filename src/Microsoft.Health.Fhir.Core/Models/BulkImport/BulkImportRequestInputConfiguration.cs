// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.Core.Models
{
    public class BulkImportRequestInputConfiguration
    {
        /// <summary>
        /// Determines the resource type of the input
        /// </summary>
        public string Type { get; set; }

        /// <summary>
        /// Determines the location of the input data.
        /// Should be a uri pointing to the input data.
        /// </summary>
        public Uri Url { get; set; }

        /// <summary>
        /// Determines the etag of resource file.
        /// </summary>
        public string Etag { get; set; }
    }
}
