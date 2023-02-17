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
        public RawResource(string data, FhirResourceFormat format, bool isMetaSet)
        {
            EnsureArg.IsNotNull(data, nameof(data));

            Data = data;
            Format = format;
            IsMetaSet = isMetaSet;
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

        /// <summary>
        /// Specifies whether the meta section in the serialized resource in Data is set correctly.
        /// We expect that for a RawResource resulting from an update, the version needs to be updated, so isMetaSet would be false.
        /// While on a RawResource resulting from a create, the version should be correct and isMetaSet would be true.
        /// </summary>
        [JsonProperty("isMetaSet")]
        public bool IsMetaSet { get; set; }
    }
}
