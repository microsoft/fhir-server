// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;

namespace Microsoft.Health.Fhir.Ignixa;

/// <summary>
/// Provides JSON serialization and deserialization for FHIR resources using Ignixa.
/// </summary>
/// <remarks>
/// <para>
/// This serializer uses <see cref="ResourceJsonNode"/> and System.Text.Json for high-performance
/// JSON processing. It does NOT depend on Firely's FhirJsonParser or FhirJsonSerializer.
/// </para>
/// <para>
/// Key features:
/// </para>
/// <list type="bullet">
/// <item><description>Zero-copy parsing to mutable JSON DOM</description></item>
/// <item><description>Async stream-based parsing for request bodies</description></item>
/// <item><description>Pretty-printing support for debugging</description></item>
/// <item><description>UTF-8 byte array serialization for optimal performance</description></item>
/// </list>
/// </remarks>
public class IgnixaJsonSerializer : IIgnixaJsonSerializer
{
    /// <summary>
    /// Parses a JSON string into a <see cref="ResourceJsonNode"/>.
    /// </summary>
    /// <param name="json">The JSON string to parse.</param>
    /// <returns>A mutable <see cref="ResourceJsonNode"/> representation of the FHIR resource.</returns>
    /// <exception cref="ArgumentNullException">Thrown when json is null.</exception>
    /// <exception cref="System.Text.Json.JsonException">Thrown when the JSON is malformed.</exception>
    public ResourceJsonNode Parse(string json)
    {
        EnsureArg.IsNotNullOrWhiteSpace(json, nameof(json));

        return JsonSourceNodeFactory.Parse<ResourceJsonNode>(json);
    }

    /// <summary>
    /// Parses a JSON string into a strongly-typed <see cref="ResourceJsonNode"/> subclass.
    /// </summary>
    /// <typeparam name="T">The target resource type (must inherit from ResourceJsonNode).</typeparam>
    /// <param name="json">The JSON string to parse.</param>
    /// <returns>A mutable resource node of the specified type.</returns>
    /// <exception cref="ArgumentNullException">Thrown when json is null.</exception>
    /// <exception cref="System.Text.Json.JsonException">Thrown when the JSON is malformed.</exception>
    public T Parse<T>(string json)
        where T : ResourceJsonNode
    {
        EnsureArg.IsNotNullOrWhiteSpace(json, nameof(json));

        return JsonSourceNodeFactory.Parse<T>(json);
    }

    /// <summary>
    /// Parses UTF-8 JSON bytes into a <see cref="ResourceJsonNode"/>.
    /// </summary>
    /// <param name="jsonBytes">UTF-8 encoded JSON bytes.</param>
    /// <returns>A mutable <see cref="ResourceJsonNode"/> representation of the FHIR resource.</returns>
    /// <exception cref="System.Text.Json.JsonException">Thrown when the JSON is malformed.</exception>
    public ResourceJsonNode Parse(ReadOnlyMemory<byte> jsonBytes)
    {
        return JsonSourceNodeFactory.Parse<ResourceJsonNode>(jsonBytes);
    }

    /// <summary>
    /// Parses UTF-8 JSON bytes into a strongly-typed <see cref="ResourceJsonNode"/> subclass.
    /// </summary>
    /// <typeparam name="T">The target resource type (must inherit from ResourceJsonNode).</typeparam>
    /// <param name="jsonBytes">UTF-8 encoded JSON bytes.</param>
    /// <returns>A mutable resource node of the specified type.</returns>
    /// <exception cref="System.Text.Json.JsonException">Thrown when the JSON is malformed.</exception>
    public T Parse<T>(ReadOnlyMemory<byte> jsonBytes)
        where T : ResourceJsonNode
    {
        return JsonSourceNodeFactory.Parse<T>(jsonBytes);
    }

    /// <summary>
    /// Asynchronously parses a JSON stream into a <see cref="ResourceJsonNode"/>.
    /// </summary>
    /// <param name="stream">The JSON stream to parse.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that resolves to a mutable <see cref="ResourceJsonNode"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when stream is null.</exception>
    /// <exception cref="System.Text.Json.JsonException">Thrown when the JSON is malformed.</exception>
    public async ValueTask<ResourceJsonNode> ParseAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        EnsureArg.IsNotNull(stream, nameof(stream));

        return await JsonSourceNodeFactory.ParseAsync<ResourceJsonNode>(stream, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Asynchronously parses a JSON stream into a strongly-typed <see cref="ResourceJsonNode"/> subclass.
    /// </summary>
    /// <typeparam name="T">The target resource type (must inherit from ResourceJsonNode).</typeparam>
    /// <param name="stream">The JSON stream to parse.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that resolves to a mutable resource node of the specified type.</returns>
    /// <exception cref="ArgumentNullException">Thrown when stream is null.</exception>
    /// <exception cref="System.Text.Json.JsonException">Thrown when the JSON is malformed.</exception>
    public async ValueTask<T> ParseAsync<T>(Stream stream, CancellationToken cancellationToken = default)
        where T : ResourceJsonNode
    {
        EnsureArg.IsNotNull(stream, nameof(stream));

        return await JsonSourceNodeFactory.ParseAsync<T>(stream, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Serializes a <see cref="ResourceJsonNode"/> to a JSON string.
    /// </summary>
    /// <param name="resource">The resource to serialize.</param>
    /// <param name="pretty">If true, the output is formatted with indentation for readability.</param>
    /// <returns>The JSON string representation of the resource.</returns>
    /// <exception cref="ArgumentNullException">Thrown when resource is null.</exception>
    public string Serialize(ResourceJsonNode resource, bool pretty = false)
    {
        EnsureArg.IsNotNull(resource, nameof(resource));

        return resource.SerializeToString(pretty);
    }

    /// <summary>
    /// Serializes a <see cref="ResourceJsonNode"/> to a UTF-8 byte array.
    /// </summary>
    /// <param name="resource">The resource to serialize.</param>
    /// <param name="pretty">If true, the output is formatted with indentation for readability.</param>
    /// <returns>UTF-8 encoded JSON bytes.</returns>
    /// <exception cref="ArgumentNullException">Thrown when resource is null.</exception>
    public ReadOnlyMemory<byte> SerializeToBytes(ResourceJsonNode resource, bool pretty = false)
    {
        EnsureArg.IsNotNull(resource, nameof(resource));

        return resource.SerializeToBytes(pretty);
    }

    /// <summary>
    /// Serializes a <see cref="ResourceJsonNode"/> directly to a stream.
    /// </summary>
    /// <param name="resource">The resource to serialize.</param>
    /// <param name="stream">The stream to write to.</param>
    /// <param name="pretty">If true, the output is formatted with indentation for readability.</param>
    /// <exception cref="ArgumentNullException">Thrown when resource or stream is null.</exception>
    public void Serialize(ResourceJsonNode resource, Stream stream, bool pretty = false)
    {
        EnsureArg.IsNotNull(resource, nameof(resource));
        EnsureArg.IsNotNull(stream, nameof(stream));

        resource.SerializeToStream(stream, pretty);
    }

    /// <summary>
    /// Serializes an <see cref="IgnixaResourceElement"/> to a JSON string.
    /// </summary>
    /// <param name="element">The resource element to serialize.</param>
    /// <param name="pretty">If true, the output is formatted with indentation for readability.</param>
    /// <returns>The JSON string representation of the resource.</returns>
    /// <exception cref="ArgumentNullException">Thrown when element is null.</exception>
    public string Serialize(IgnixaResourceElement element, bool pretty = false)
    {
        EnsureArg.IsNotNull(element, nameof(element));

        return Serialize(element.ResourceNode, pretty);
    }

    /// <summary>
    /// Serializes an <see cref="IgnixaResourceElement"/> directly to a stream.
    /// </summary>
    /// <param name="element">The resource element to serialize.</param>
    /// <param name="stream">The stream to write to.</param>
    /// <param name="pretty">If true, the output is formatted with indentation for readability.</param>
    /// <exception cref="ArgumentNullException">Thrown when element or stream is null.</exception>
    public void Serialize(IgnixaResourceElement element, Stream stream, bool pretty = false)
    {
        EnsureArg.IsNotNull(element, nameof(element));
        EnsureArg.IsNotNull(stream, nameof(stream));

        Serialize(element.ResourceNode, stream, pretty);
    }
}
