// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Ignixa;

/// <summary>
/// ASP.NET Core input formatter that reads FHIR JSON requests using Ignixa for parsing.
/// </summary>
/// <remarks>
/// <para>
/// This formatter uses Ignixa's <see cref="Ignixa.Serialization.JsonSourceNodeFactory"/> for high-performance
/// JSON parsing, with support for returning both Ignixa types and Firely SDK types.
/// </para>
/// <para>
/// Supported output types:
/// </para>
/// <list type="bullet">
/// <item><description><see cref="ResourceJsonNode"/> - Native Ignixa type</description></item>
/// <item><description><see cref="IgnixaResourceElement"/> - Ignixa wrapper with schema</description></item>
/// <item><description><see cref="ResourceElement"/> - Core ResourceElement type (for controller binding)</description></item>
/// <item><description><see cref="Resource"/> - Firely SDK type (for compatibility)</description></item>
/// </list>
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
    private readonly FhirJsonParser _firelyParser;
    private readonly IServiceProvider _serviceProvider;
    private ISchema? _cachedSchema;

    /// <summary>
    /// Initializes a new instance of the <see cref="IgnixaFhirJsonInputFormatter"/> class.
    /// </summary>
    /// <param name="serializer">The Ignixa JSON serializer.</param>
    /// <param name="firelyParser">The Firely JSON parser for compatibility mode.</param>
    /// <param name="serviceProvider">The service provider for resolving ISchema lazily.</param>
    public IgnixaFhirJsonInputFormatter(
        IIgnixaJsonSerializer serializer,
        FhirJsonParser firelyParser,
        IServiceProvider serviceProvider)
    {
        EnsureArg.IsNotNull(serializer, nameof(serializer));
        EnsureArg.IsNotNull(firelyParser, nameof(firelyParser));
        EnsureArg.IsNotNull(serviceProvider, nameof(serviceProvider));

        _serializer = serializer;
        _firelyParser = firelyParser;
        _serviceProvider = serviceProvider;

        SupportedEncodings.Add(UTF8EncodingWithoutBOM);
        SupportedEncodings.Add(UTF16EncodingLittleEndian);

        // FHIR-specific content type
        SupportedMediaTypes.Add(FhirJsonContentType);

        // Standard JSON content types
        SupportedMediaTypes.Add("application/json");
        SupportedMediaTypes.Add("text/json");
        SupportedMediaTypes.Add("application/*+json");
    }

    /// <summary>
    /// Gets the Ignixa schema, resolving it lazily from the service provider.
    /// </summary>
    private ISchema Schema
    {
        get
        {
            if (_cachedSchema == null)
            {
                // Resolve IIgnixaSchemaContext from the service provider
                // This avoids circular dependencies and ensures proper DI ordering
                var schemaContext = _serviceProvider.GetService<IIgnixaSchemaContext>();
                if (schemaContext != null)
                {
                    _cachedSchema = schemaContext.Schema;
                }
                else
                {
                    throw new InvalidOperationException(
                        "IIgnixaSchemaContext is not registered. Ensure FhirModule is loaded before using the Ignixa input formatter.");
                }
            }

            return _cachedSchema;
        }
    }

    /// <inheritdoc />
    protected override bool CanReadType(Type type)
    {
        EnsureArg.IsNotNull(type, nameof(type));

        // Support reading to ResourceJsonNode, IgnixaResourceElement, ResourceElement, or Firely Resource
        return typeof(ResourceJsonNode).IsAssignableFrom(type) ||
               typeof(IgnixaResourceElement).IsAssignableFrom(type) ||
               typeof(ResourceElement).IsAssignableFrom(type) ||
               typeof(Resource).IsAssignableFrom(type);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// This implementation reads the request body as a stream and parses it using
    /// Ignixa's async JSON parser for optimal performance with large resources.
    /// </para>
    /// <para>
    /// When the target type is a Firely <see cref="Resource"/>, the JSON is first parsed
    /// with Ignixa, then serialized back and parsed with Firely for compatibility.
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
        var targetType = context.ModelType;

        // Check what type we need to return
        bool needsFirelyResource = typeof(Resource).IsAssignableFrom(targetType);
        bool needsResourceElement = typeof(ResourceElement).IsAssignableFrom(targetType) && !typeof(IgnixaResourceElement).IsAssignableFrom(targetType);
        bool needsIgnixaResourceElement = typeof(IgnixaResourceElement).IsAssignableFrom(targetType);

        // For Firely Resource types, we parse with Ignixa first for validation,
        // then re-parse with Firely to get the strongly-typed object.
        // This ensures consistent parsing behavior while leveraging Ignixa's speed.
        ResourceJsonNode? ignixa = null;
        Resource? firelyResource = null;
        Exception? parseException = null;

        try
        {
            // Read the request body into a buffer so we can parse it twice if needed
            using var memoryStream = new MemoryStream();
            await request.Body.CopyToAsync(memoryStream, context.HttpContext.RequestAborted).ConfigureAwait(false);
            memoryStream.Position = 0;

            // Parse with Ignixa first
            ignixa = await _serializer.ParseAsync(memoryStream, context.HttpContext.RequestAborted).ConfigureAwait(false);

            // If we need a Firely Resource, use the Firely parser on the same JSON
            if (needsFirelyResource && ignixa != null)
            {
                // Get the JSON string from Ignixa and parse with Firely
                var jsonString = _serializer.Serialize(ignixa);
                firelyResource = await _firelyParser.ParseAsync<Resource>(jsonString).ConfigureAwait(false);
            }
        }
        catch (System.Text.Json.JsonException ex)
        {
            parseException = ex;
        }
        catch (FormatException ex)
        {
            parseException = ex;
        }
        catch (InvalidOperationException ex)
        {
            parseException = ex;
        }

        // Handle null result (empty input)
        if (ignixa == null && parseException == null && !context.TreatEmptyInputAsDefaultValue)
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

        // Return the appropriate type
        if (needsFirelyResource && firelyResource != null)
        {
            return await InputFormatterResult.SuccessAsync(firelyResource).ConfigureAwait(false);
        }

        if (needsResourceElement && ignixa != null)
        {
            // Create an IgnixaResourceElement to get ITypedElement, then wrap in ResourceElement
            // This preserves the ResourceJsonNode in ResourceInstance for GetIgnixaNode() access
            var ignixaElement = new IgnixaResourceElement(ignixa, Schema);
            var resourceElement = new ResourceElement(ignixaElement.ToTypedElement(), ignixa);
            return await InputFormatterResult.SuccessAsync(resourceElement).ConfigureAwait(false);
        }

        if (needsIgnixaResourceElement && ignixa != null)
        {
            // Return IgnixaResourceElement directly
            var ignixaResourceElement = new IgnixaResourceElement(ignixa, Schema);
            return await InputFormatterResult.SuccessAsync(ignixaResourceElement).ConfigureAwait(false);
        }

        if (ignixa != null)
        {
            return await InputFormatterResult.SuccessAsync(ignixa).ConfigureAwait(false);
        }

        return await InputFormatterResult.FailureAsync().ConfigureAwait(false);
    }
}
