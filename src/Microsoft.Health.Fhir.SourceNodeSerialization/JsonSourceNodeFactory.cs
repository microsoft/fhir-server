// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;
using Hl7.Fhir.ElementModel;
using Microsoft.Health.Fhir.SourceNodeSerialization.SourceNodes;
using Microsoft.Health.Fhir.SourceNodeSerialization.SourceNodes.Models;

namespace Microsoft.Health.Fhir.SourceNodeSerialization;

public static class JsonSourceNodeFactory
{
    private static readonly JsonSerializerOptions _jsonSerializerOptions = new()
    {
        AllowTrailingCommas = false,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        Encoder = JavaScriptEncoder.Default,
    };

    public static ISourceNode Parse(string json, string name = null)
    {
        ResourceJsonNode resource = JsonSerializer.Deserialize<ResourceJsonNode>(json, _jsonSerializerOptions);
        return new ReflectedSourceNode(resource, name);
    }

    public static TResource ParseJsonNode<TResource>(string json)
        where TResource : ResourceJsonNode
    {
        TResource resource = JsonSerializer.Deserialize<TResource>(json, _jsonSerializerOptions);
        return resource;
    }

    public static async ValueTask<ISourceNode> Parse(Stream jsonReader, string name = null)
    {
        ResourceJsonNode resource = await JsonSerializer.DeserializeAsync<ResourceJsonNode>(jsonReader, _jsonSerializerOptions);
        return new ReflectedSourceNode(resource, name);
    }

    public static async ValueTask<T> Parse<T>(Stream jsonReader, string name = null)
        where T : ResourceJsonNode
    {
        T resource = await JsonSerializer.DeserializeAsync<T>(jsonReader, _jsonSerializerOptions);
        return resource;
    }

    public static ISourceNode Create(ResourceJsonNode resource)
    {
        return new ReflectedSourceNode(resource, null);
    }

    public static string SerializeToString(this ResourceJsonNode resource)
    {
        return JsonSerializer.Serialize(resource, _jsonSerializerOptions);
    }
}
