// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Hl7.Fhir.Rest;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Microsoft.Health.Fhir.Core.Features.Persistence
{
    public class RawResource
    {
        public RawResource(string data, ResourceFormat format)
        {
            EnsureArg.IsNotNullOrEmpty(data, nameof(data));

            Data = data;
            Format = format;
        }

        [JsonConstructor]
        protected RawResource()
        {
        }

        [JsonProperty("data")]
        public string Data { get; protected set; }

        [JsonProperty("format")]
        [JsonConverter(typeof(StringEnumConverter))]
        public ResourceFormat Format { get; protected set; }
    }
}
