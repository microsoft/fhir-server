// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Serialization.SourceNodes;

namespace Microsoft.Health.Fhir.Ignixa;

/// <summary>
/// Defines the contract for JSON serialization and deserialization of FHIR resources using Ignixa.
/// </summary>
public interface IIgnixaJsonSerializer
{
    /// <summary>
    /// Parses a JSON string into a <see cref="ResourceJsonNode"/>.
    /// </summary>
    /// <param name="json">The JSON string to parse.</param>
    /// <returns>A mutable <see cref="ResourceJsonNode"/> representation of the FHIR resource.</returns>
    ResourceJsonNode Parse(string json);

    /// <summary>
    /// Parses a JSON string into a strongly-typed <see cref="ResourceJsonNode"/> subclass.
    /// </summary>
    /// <typeparam name="T">The target resource type (must inherit from ResourceJsonNode).</typeparam>
    /// <param name="json">The JSON string to parse.</param>
    /// <returns>A mutable resource node of the specified type.</returns>
    T Parse<T>(string json)
        where T : ResourceJsonNode;

    /// <summary>
    /// Parses UTF-8 JSON bytes into a <see cref="ResourceJsonNode"/>.
    /// </summary>
    /// <param name="jsonBytes">UTF-8 encoded JSON bytes.</param>
    /// <returns>A mutable <see cref="ResourceJsonNode"/> representation of the FHIR resource.</returns>
    ResourceJsonNode Parse(ReadOnlyMemory<byte> jsonBytes);

    /// <summary>
    /// Parses UTF-8 JSON bytes into a strongly-typed <see cref="ResourceJsonNode"/> subclass.
    /// </summary>
    /// <typeparam name="T">The target resource type (must inherit from ResourceJsonNode).</typeparam>
    /// <param name="jsonBytes">UTF-8 encoded JSON bytes.</param>
    /// <returns>A mutable resource node of the specified type.</returns>
    T Parse<T>(ReadOnlyMemory<byte> jsonBytes)
        where T : ResourceJsonNode;

    /// <summary>
    /// Asynchronously parses a JSON stream into a <see cref="ResourceJsonNode"/>.
    /// </summary>
    /// <param name="stream">The JSON stream to parse.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that resolves to a mutable <see cref="ResourceJsonNode"/>.</returns>
    ValueTask<ResourceJsonNode> ParseAsync(Stream stream, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously parses a JSON stream into a strongly-typed <see cref="ResourceJsonNode"/> subclass.
    /// </summary>
    /// <typeparam name="T">The target resource type (must inherit from ResourceJsonNode).</typeparam>
    /// <param name="stream">The JSON stream to parse.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that resolves to a mutable resource node of the specified type.</returns>
    ValueTask<T> ParseAsync<T>(Stream stream, CancellationToken cancellationToken = default)
        where T : ResourceJsonNode;

    /// <summary>
    /// Serializes a <see cref="ResourceJsonNode"/> to a JSON string.
    /// </summary>
    /// <param name="resource">The resource to serialize.</param>
    /// <param name="pretty">If true, the output is formatted with indentation for readability.</param>
    /// <returns>The JSON string representation of the resource.</returns>
    string Serialize(ResourceJsonNode resource, bool pretty = false);

    /// <summary>
    /// Serializes a <see cref="ResourceJsonNode"/> to a UTF-8 byte array.
    /// </summary>
    /// <param name="resource">The resource to serialize.</param>
    /// <param name="pretty">If true, the output is formatted with indentation for readability.</param>
    /// <returns>UTF-8 encoded JSON bytes.</returns>
    ReadOnlyMemory<byte> SerializeToBytes(ResourceJsonNode resource, bool pretty = false);

    /// <summary>
    /// Serializes a <see cref="ResourceJsonNode"/> directly to a stream.
    /// </summary>
    /// <param name="resource">The resource to serialize.</param>
    /// <param name="stream">The stream to write to.</param>
    /// <param name="pretty">If true, the output is formatted with indentation for readability.</param>
    void Serialize(ResourceJsonNode resource, Stream stream, bool pretty = false);

    /// <summary>
    /// Serializes an <see cref="IgnixaResourceElement"/> to a JSON string.
    /// </summary>
    /// <param name="element">The resource element to serialize.</param>
    /// <param name="pretty">If true, the output is formatted with indentation for readability.</param>
    /// <returns>The JSON string representation of the resource.</returns>
    string Serialize(IgnixaResourceElement element, bool pretty = false);

    /// <summary>
    /// Serializes an <see cref="IgnixaResourceElement"/> directly to a stream.
    /// </summary>
    /// <param name="element">The resource element to serialize.</param>
    /// <param name="stream">The stream to write to.</param>
    /// <param name="pretty">If true, the output is formatted with indentation for readability.</param>
    void Serialize(IgnixaResourceElement element, Stream stream, bool pretty = false);
}
