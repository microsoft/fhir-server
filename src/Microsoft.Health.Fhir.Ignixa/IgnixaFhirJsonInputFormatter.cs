// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using System.Text;
using EnsureThat;
using Ignixa.Serialization.SourceNodes;
using Microsoft.AspNetCore.Mvc.Formatters;

namespace Microsoft.Health.Fhir.Ignixa;

/// <summary>
/// ASP.NET Core input formatter that reads FHIR JSON requests and parses them to <see cref="ResourceJsonNode"/>.
/// </summary>
/// <remarks>
/// <para>
/// This formatter uses Ignixa's <see cref="Ignixa.Serialization.JsonSourceNodeFactory"/> for high-performance
/// JSON parsing. It does NOT depend on Firely's FhirJsonParser.
/// </para>
/// <para>
/// Supported content types:
/// </para>
/// <list type="bullet">
/// <item><description>application/fhir+json</description></item>
/// <item><description>application/json</description></item>
/// <item><description>text/json</description></item>
/// <item><description>application/*+json</description></item>
/// </list>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated via dependency injection")]
internal sealed class IgnixaFhirJsonInputFormatter : TextInputFormatter
{
    /// <summary>
    /// The FHIR JSON content type.
    /// </summary>
    public const string FhirJsonContentType = "application/fhir+json";

    private readonly IIgnixaJsonSerializer _serializer;

    /// <summary>
    /// Initializes a new instance of the <see cref="IgnixaFhirJsonInputFormatter"/> class.
    /// </summary>
    /// <param name="serializer">The Ignixa JSON serializer.</param>
    public IgnixaFhirJsonInputFormatter(IIgnixaJsonSerializer serializer)
    {
        EnsureArg.IsNotNull(serializer, nameof(serializer));

        _serializer = serializer;

        SupportedEncodings.Add(UTF8EncodingWithoutBOM);
        SupportedEncodings.Add(UTF16EncodingLittleEndian);

        // FHIR-specific content type
        SupportedMediaTypes.Add(FhirJsonContentType);

        // Standard JSON content types
        SupportedMediaTypes.Add("application/json");
        SupportedMediaTypes.Add("text/json");
        SupportedMediaTypes.Add("application/*+json");
    }

    /// <inheritdoc />
    protected override bool CanReadType(Type type)
    {
        EnsureArg.IsNotNull(type, nameof(type));

        // Support reading to ResourceJsonNode or IgnixaResourceElement
        return typeof(ResourceJsonNode).IsAssignableFrom(type) ||
               typeof(IgnixaResourceElement).IsAssignableFrom(type);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// This implementation reads the request body as a stream and parses it using
    /// Ignixa's async JSON parser for optimal performance with large resources.
    /// </para>
    /// <para>
    /// Error handling follows the existing FHIR server patterns, adding model state
    /// errors that can be returned as FHIR OperationOutcome responses.
    /// </para>
    /// </remarks>
    public override async Task<InputFormatterResult> ReadRequestBodyAsync(InputFormatterContext context, Encoding encoding)
    {
        EnsureArg.IsNotNull(context, nameof(context));
        EnsureArg.IsNotNull(encoding, nameof(encoding));

        var request = context.HttpContext.Request;
        ResourceJsonNode? resource = null;
        Exception? parseException = null;

        try
        {
            // Use the request body stream directly for async parsing
            resource = await _serializer.ParseAsync(request.Body, context.HttpContext.RequestAborted).ConfigureAwait(false);
        }
        catch (System.Text.Json.JsonException ex)
        {
            parseException = ex;
        }
        catch (InvalidOperationException ex)
        {
            parseException = ex;
        }

        // Handle null result (empty input)
        if (resource == null && parseException == null && !context.TreatEmptyInputAsDefaultValue)
        {
            return await InputFormatterResult.NoValueAsync().ConfigureAwait(false);
        }

        // Handle parse errors
        if (parseException != null)
        {
            context.ModelState.TryAddModelError(
                string.Empty,
                $"Error parsing FHIR JSON: {parseException.Message}");

            return await InputFormatterResult.FailureAsync().ConfigureAwait(false);
        }

        // Return the parsed resource
        if (resource != null)
        {
            return await InputFormatterResult.SuccessAsync(resource).ConfigureAwait(false);
        }

        return await InputFormatterResult.FailureAsync().ConfigureAwait(false);
    }
}
