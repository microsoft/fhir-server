// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.Health.Fhir.RegisterAndMonitorImport
{
#pragma warning disable CA1812 // Avoid uninstantiated internal classes
    internal sealed class ImportResponse
    {
        [JsonPropertyName("error")]
        public List<Json> Error { get; set; } = new();

        [JsonPropertyName("output")]
        public List<Json> Output { get; set; } = new();

        public sealed class Json
        {
            [JsonPropertyName("type")]
            public string Type { get; set; } = string.Empty;

            [JsonPropertyName("count")]
            public int Count { get; set; }

            [JsonPropertyName("inputUrl")]
            public string InputUrl { get; set; } = string.Empty;
        }
    }
#pragma warning disable CA1812 // Avoid uninstantiated internal classes
}
