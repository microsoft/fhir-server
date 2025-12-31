// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using System.Text;
using EnsureThat;
using Ignixa.Serialization.SourceNodes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Formatters;

namespace Microsoft.Health.Fhir.Ignixa;

/// <summary>
/// ASP.NET Core output formatter that writes FHIR resources as JSON using Ignixa serialization.
/// </summary>
/// <remarks>
/// <para>
/// This formatter uses Ignixa's <see cref="Ignixa.Serialization.JsonSourceNodeFactory"/> for high-performance
/// JSON serialization. It does NOT depend on Firely's FhirJsonSerializer.
/// </para>
/// <para>
/// Supported output types:
/// </para>
/// <list type="bullet">
/// <item><description><see cref="ResourceJsonNode"/> - Direct Ignixa resource node</description></item>
/// <item><description><see cref="IgnixaResourceElement"/> - Wrapper with schema awareness</description></item>
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

    /// <summary>
    /// Initializes a new instance of the <see cref="IgnixaFhirJsonOutputFormatter"/> class.
    /// </summary>
    /// <param name="serializer">The Ignixa JSON serializer.</param>
    public IgnixaFhirJsonOutputFormatter(IIgnixaJsonSerializer serializer)
    {
        EnsureArg.IsNotNull(serializer, nameof(serializer));

        _serializer = serializer;

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

        // Support writing ResourceJsonNode or IgnixaResourceElement
        return typeof(ResourceJsonNode).IsAssignableFrom(type) ||
               typeof(IgnixaResourceElement).IsAssignableFrom(type);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// This implementation writes the resource directly to the response body stream
    /// for optimal performance. Pretty-printing is controlled via the <c>_pretty</c>
    /// query parameter following FHIR conventions.
    /// </para>
    /// </remarks>
    public override async Task WriteResponseBodyAsync(OutputFormatterWriteContext context, Encoding selectedEncoding)
    {
        EnsureArg.IsNotNull(context, nameof(context));
        EnsureArg.IsNotNull(selectedEncoding, nameof(selectedEncoding));

        var response = context.HttpContext.Response;
        var pretty = GetPrettyParameter(context.HttpContext);

        ResourceJsonNode? resourceNode = null;

        // Extract the ResourceJsonNode from the object
        if (context.Object is IgnixaResourceElement element)
        {
            resourceNode = element.ResourceNode;
        }
        else if (context.Object is ResourceJsonNode node)
        {
            resourceNode = node;
        }

        if (resourceNode == null)
        {
            // This shouldn't happen if CanWriteType is correct, but handle gracefully
            await response.WriteAsync("{}", selectedEncoding).ConfigureAwait(false);
            return;
        }

        // Write directly to the response body stream
        _serializer.Serialize(resourceNode, response.Body, pretty);

        // Ensure the stream is flushed
        await response.Body.FlushAsync(context.HttpContext.RequestAborted).ConfigureAwait(false);
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
