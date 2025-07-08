// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Microsoft.Health.Fhir.SourceNodeSerialization.SourceNodes.Models;

public class SearchParameterJsonNode : ResourceJsonNode
{
    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("code")]
    public string Code { get; set; }

    [JsonPropertyName("description")]
    public string Description { get; set; }

    [JsonPropertyName("url")]
    [SuppressMessage("Design", "CA1056:URI-like properties should not be strings", Justification = "This is a POCO.")]
    public string Url { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; }

    [JsonPropertyName("expression")]
    public string Expression { get; set; }

    [JsonPropertyName("base")]
    public IReadOnlyList<string> Base { get; set; }

    [JsonPropertyName("target")]
    public IReadOnlyList<string> Target { get; set; }
}
