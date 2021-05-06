// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    public class ImportOperationOutcome
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("count")]
        public long Count { get; set; }

        [JsonProperty("inputUrl")]
        public Uri InputUrl { get; set; }

#pragma warning disable CA1056 // URI-like properties should not be strings
        [JsonProperty("url")]
        public string Url { get; set; }
#pragma warning restore CA1056 // URI-like properties should not be strings
    }
}
