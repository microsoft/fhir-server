// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    public class ImportFailedOperationOutcome
    {
        /// <summary>
        /// Resource Type
        /// </summary>
        [JsonProperty("type")]
        public string Type { get; set; }

        /// <summary>
        /// Processing resource count.
        /// </summary>
        [JsonProperty("count")]
        public long Count { get; set; }

        /// <summary>
        /// Input resource url.
        /// </summary>
        [JsonProperty("inputUrl")]
        public Uri InputUrl { get; set; }

        /// <summary>
        /// Extension detail file.
        /// </summary>
#pragma warning disable CA1056 // URI-like properties should not be strings
        [JsonProperty("url")]
        public string Url { get; set; }
#pragma warning restore CA1056 // URI-like properties should not be strings
    }
}
