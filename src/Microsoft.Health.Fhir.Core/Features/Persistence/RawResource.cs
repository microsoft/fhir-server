// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Health.Fhir.Core.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Microsoft.Health.Fhir.Core.Features.Persistence
{
    public class RawResource
    {
        public RawResource(string data, FhirResourceFormat format, bool versionSet, bool lastUpdatedSet)
        {
            EnsureArg.IsNotNullOrEmpty(data, nameof(data));

            Data = data;
            Format = format;
            VersionSet = versionSet;
            LastUpdatedSet = lastUpdatedSet;
        }

        [JsonConstructor]
        protected RawResource()
        {
        }

        [JsonProperty("data")]
        public string Data { get; protected set; }

        [JsonProperty("format")]
        [JsonConverter(typeof(StringEnumConverter))]
        public FhirResourceFormat Format { get; protected set; }

        [JsonProperty("versionSet")]
        public bool VersionSet { get; protected set; }

        [JsonProperty("lastUpdatedSet")]
        public bool LastUpdatedSet { get; protected set; }
    }
}
