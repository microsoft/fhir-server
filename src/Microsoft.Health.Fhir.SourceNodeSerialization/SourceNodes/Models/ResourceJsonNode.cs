// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hl7.Fhir.ElementModel;

namespace Microsoft.Health.Fhir.SourceNodeSerialization.SourceNodes.Models;

[SuppressMessage("Design", "CA2227", Justification = "POCO style model")]
public class ResourceJsonNode : IExtensionData, IResourceNode
{
    [JsonPropertyName("resourceType")]
    public string ResourceType { get; set; }

    [JsonPropertyName("id")]
    public string Id { get; set; }

    [JsonPropertyName("meta")]
    public MetaJsonNode Meta { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; set; }

    public ISourceNode ToSourceNode()
    {
        return new ReflectedSourceNode(this, null);
    }
}
