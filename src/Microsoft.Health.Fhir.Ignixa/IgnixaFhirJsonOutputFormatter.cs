// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using System.Text;
using EnsureThat;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Ignixa.Serialization.SourceNodes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Models;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Ignixa;

/// <summary>
/// ASP.NET Core output formatter that writes FHIR resources as JSON using Ignixa serialization.
/// </summary>
/// <remarks>
/// <para>
/// This formatter uses Ignixa's <see cref="Ignixa.Serialization.JsonSourceNodeFactory"/> for high-performance
/// JSON serialization. It provides compatibility with both Ignixa and Firely SDK types.
/// </para>
/// <para>
/// Supported output types:
/// </para>
/// <list type="bullet">
/// <item><description><see cref="ResourceJsonNode"/> - Native Ignixa resource node</description></item>
/// <item><description><see cref="IgnixaResourceElement"/> - Ignixa wrapper with schema awareness</description></item>
/// <item><description><see cref="Resource"/> - Firely SDK Resource (converted via serialization)</description></item>
/// <item><description><see cref="RawResourceElement"/> - Raw JSON resource from persistence layer</description></item>
/// </list>
/// <para>
/// Pretty-printing is controlled via the <c>_pretty</c> query parameter.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated via dependency injection")]
internal sealed class IgnixaFhirJsonOutputFormatter : TextOutputFormatter
{
    /// <summary>
    /// The FHIR JSON content type.
    /// </summary>
    public const string FhirJsonContentType = "application/fhir+json";

    private readonly IIgnixaJsonSerializer _serializer;
    private readonly FhirJsonSerializer _firelySerializer;

    /// <summary>
    /// Initializes a new instance of the <see cref="IgnixaFhirJsonOutputFormatter"/> class.
    /// </summary>
    /// <param name="serializer">The Ignixa JSON serializer.</param>
    /// <param name="firelySerializer">The Firely JSON serializer for compatibility mode.</param>
    public IgnixaFhirJsonOutputFormatter(IIgnixaJsonSerializer serializer, FhirJsonSerializer firelySerializer)
    {
        EnsureArg.IsNotNull(serializer, nameof(serializer));
        EnsureArg.IsNotNull(firelySerializer, nameof(firelySerializer));

        _serializer = serializer;
        _firelySerializer = firelySerializer;

        SupportedEncodings.Add(Encoding.UTF8);
        SupportedEncodings.Add(Encoding.Unicode);

        // FHIR-specific content type
        SupportedMediaTypes.Add(FhirJsonContentType);

        // Standard JSON content types
        SupportedMediaTypes.Add("application/json");
        SupportedMediaTypes.Add("text/json");
        SupportedMediaTypes.Add("application/*+json");
    }

    /// <inheritdoc />
    protected override bool CanWriteType(Type? type)
    {
        if (type == null)
        {
            return false;
        }

        // Support writing Ignixa types, Firely Resource, and RawResourceElement
        return typeof(ResourceJsonNode).IsAssignableFrom(type) ||
               typeof(IgnixaResourceElement).IsAssignableFrom(type) ||
               typeof(Resource).IsAssignableFrom(type) ||
               typeof(RawResourceElement).IsAssignableFrom(type);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// This implementation writes the resource directly to the response body stream
    /// for optimal performance. Pretty-printing is controlled via the <c>_pretty</c>
    /// query parameter following FHIR conventions.
    /// </para>
    /// <para>
    /// For <see cref="RawResourceElement"/>, the raw JSON is written directly if available
    /// in JSON format, providing zero-copy output for database reads.
    /// </para>
    /// <para>
    /// For Firely <see cref="Resource"/> types, the resource is first serialized to JSON
    /// using Firely, then re-parsed and written with Ignixa for consistent output formatting.
    /// </para>
    /// </remarks>
    public override async Task WriteResponseBodyAsync(OutputFormatterWriteContext context, Encoding selectedEncoding)
    {
        EnsureArg.IsNotNull(context, nameof(context));
        EnsureArg.IsNotNull(selectedEncoding, nameof(selectedEncoding));

        var response = context.HttpContext.Response;
        var pretty = GetPrettyParameter(context.HttpContext);

        // Handle RawResourceElement - write raw JSON directly for best performance
        if (context.Object is RawResourceElement rawElement)
        {
            await WriteRawResourceAsync(rawElement, response, pretty, selectedEncoding).ConfigureAwait(false);
            return;
        }

        ResourceJsonNode? resourceNode = null;

        // Extract or convert to ResourceJsonNode
        if (context.Object is IgnixaResourceElement element)
        {
            resourceNode = element.ResourceNode;
        }
        else if (context.Object is ResourceJsonNode node)
        {
            resourceNode = node;
        }
        else if (context.Object is Resource firelyResource)
        {
            // Convert Firely Resource to Ignixa ResourceJsonNode via JSON serialization
            resourceNode = ConvertFirelyToIgnixa(firelyResource);
        }

        if (resourceNode == null)
        {
            // This shouldn't happen if CanWriteType is correct, but handle gracefully
            await response.WriteAsync("{}", selectedEncoding).ConfigureAwait(false);
            return;
        }

        // Write directly to the response body stream using Ignixa
        _serializer.Serialize(resourceNode, response.Body, pretty);

        // Ensure the stream is flushed
        await response.Body.FlushAsync(context.HttpContext.RequestAborted).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes a <see cref="RawResourceElement"/> directly to the response.
    /// </summary>
    /// <param name="rawElement">The raw resource element containing JSON data.</param>
    /// <param name="response">The HTTP response.</param>
    /// <param name="pretty">Whether to format the output with indentation.</param>
    /// <param name="encoding">The encoding to use.</param>
    private async Task WriteRawResourceAsync(RawResourceElement rawElement, HttpResponse response, bool pretty, Encoding encoding)
    {
        // Check if the raw resource is in JSON format
        if (rawElement.RawResource.Format == FhirResourceFormat.Json)
        {
            var rawJson = rawElement.RawResource.Data;

            // If pretty-printing is requested, we need to reformat the JSON
            if (pretty)
            {
                // Parse with Ignixa and re-serialize with indentation
                var resourceNode = _serializer.Parse(rawJson);
                _serializer.Serialize(resourceNode, response.Body, pretty: true);
            }
            else
            {
                // Write raw JSON directly - zero copy for best performance
                await response.WriteAsync(rawJson, encoding).ConfigureAwait(false);
            }
        }
        else
        {
            // XML format - need to convert to JSON
            // For now, this is not supported - would need XML parser
            await response.WriteAsync("{\"error\": \"XML format not supported for direct output\"}", encoding).ConfigureAwait(false);
        }

        await response.Body.FlushAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Converts a Firely <see cref="Resource"/> to an Ignixa <see cref="ResourceJsonNode"/>.
    /// </summary>
    /// <param name="resource">The Firely resource to convert.</param>
    /// <returns>The equivalent Ignixa resource node.</returns>
    private ResourceJsonNode ConvertFirelyToIgnixa(Resource resource)
    {
        // Serialize with Firely, then parse with Ignixa
        var json = _firelySerializer.SerializeToString(resource);
        return _serializer.Parse(json);
    }

    /// <summary>
    /// Gets the value of the <c>_pretty</c> query parameter.
    /// </summary>
    /// <param name="httpContext">The HTTP context.</param>
    /// <returns>True if pretty-printing is requested; otherwise false.</returns>
    private static bool GetPrettyParameter(HttpContext httpContext)
    {
        // Check for _pretty query parameter (FHIR standard)
        if (httpContext.Request.Query.TryGetValue("_pretty", out var prettyValue))
        {
            if (bool.TryParse(prettyValue.FirstOrDefault(), out var pretty))
            {
                return pretty;
            }

            // FHIR allows "true" or "false" as values
            var value = prettyValue.FirstOrDefault()?.ToLowerInvariant();
            return value == "true";
        }

        return false;
    }
}
