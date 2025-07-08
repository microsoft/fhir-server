// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.Health.Fhir.SourceNodeSerialization.SourceNodes.Models;

public class ExtensionJsonNode : IExtensionData
{
    [SuppressMessage("Design", "CA1056:URI-like properties should not be strings", Justification = "This is a POCO.")]
    [JsonPropertyName("url")]
    public string Url { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; set; }
}
