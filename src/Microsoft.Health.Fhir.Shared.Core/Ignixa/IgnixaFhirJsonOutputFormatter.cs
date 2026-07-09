// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using EnsureThat;
using Hl7.Fhir.Introspection;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Hl7.Fhir.Serialization;
using Hl7.Fhir.Specification;
using Ignixa.Serialization.SourceNodes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Sdk;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using Newtonsoft.Json;
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
    private readonly IModelInfoProvider _modelInfoProvider;
    private readonly ISdkFallbackGuard _fallbackGuard;
    private static readonly FhirJsonParser Parser = new();
    private const string SubsettedTagSystem = "http://terminology.hl7.org/CodeSystem/v3-ObservationValue";
    private const string SubsettedTagCode = "SUBSETTED";

    /// <summary>
    /// Initializes a new instance of the <see cref="IgnixaFhirJsonOutputFormatter"/> class.
    /// </summary>
    /// <param name="serializer">The Ignixa JSON serializer.</param>
    /// <param name="firelySerializer">The Firely JSON serializer for compatibility mode.</param>
    /// <param name="modelInfoProvider">FHIR model information provider used for projection.</param>
    /// <param name="fallbackGuard">The SDK fallback guard.</param>
    public IgnixaFhirJsonOutputFormatter(
        IIgnixaJsonSerializer serializer,
        FhirJsonSerializer firelySerializer,
        IModelInfoProvider modelInfoProvider,
        ISdkFallbackGuard fallbackGuard)
    {
        _serializer = EnsureArg.IsNotNull(serializer, nameof(serializer));
        _firelySerializer = EnsureArg.IsNotNull(firelySerializer, nameof(firelySerializer));
        _modelInfoProvider = EnsureArg.IsNotNull(modelInfoProvider, nameof(modelInfoProvider));
        _fallbackGuard = EnsureArg.IsNotNull(fallbackGuard, nameof(fallbackGuard));

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
    /// For Firely <see cref="Resource"/> types, the resource is written directly with Firely's
    /// JSON serializer so FHIR projection parameters are applied consistently.
    /// </para>
    /// </remarks>
    public override async Task WriteResponseBodyAsync(OutputFormatterWriteContext context, Encoding selectedEncoding)
    {
        EnsureArg.IsNotNull(context, nameof(context));
        EnsureArg.IsNotNull(selectedEncoding, nameof(selectedEncoding));

        var response = context.HttpContext.Response;
        var pretty = GetPrettyParameter(context.HttpContext);
        var elementsSearchParameter = GetElementsOrDefault(context.HttpContext);
        var summarySearchParameter = GetSummaryTypeOrDefault(context.HttpContext);
        var hasElements = elementsSearchParameter?.Any() == true;
        var hasProjection = hasElements || summarySearchParameter != SummaryType.False;

        // Handle RawResourceElement - write raw JSON directly for best performance
        if (context.Object is RawResourceElement rawElement)
        {
            if (hasProjection && rawElement.RawResource.Format == FhirResourceFormat.Json)
            {
                _fallbackGuard.FirelyFallback("Ignixa output projection", "_summary or _elements projection is not implemented natively on RawResourceElement.");
                using var stringReader = new StringReader(rawElement.RawResource.Data);
                using var jsonReader = new JsonTextReader(stringReader);
                var rawResource = await Parser.ParseAsync<Resource>(jsonReader).ConfigureAwait(false);
                await WriteFirelyResourceAsync(rawResource, response, pretty, selectedEncoding, summarySearchParameter, GetProjectedElements(rawResource, elementsSearchParameter)).ConfigureAwait(false);
                return;
            }

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
            // Write Firely JSON directly to the response stream — avoids the
            // previous triple-hop (Firely serialize → Ignixa parse → Ignixa serialize).
            await WriteFirelyResourceAsync(firelyResource, response, pretty, selectedEncoding, summarySearchParameter, GetProjectedElements(firelyResource, elementsSearchParameter)).ConfigureAwait(false);
            return;
        }

        if (resourceNode == null)
        {
            // This shouldn't happen if CanWriteType is correct, but handle gracefully
            await response.WriteAsync("{}", selectedEncoding).ConfigureAwait(false);
            return;
        }

        if (hasProjection)
        {
            resourceNode = ProjectResource(resourceNode, elementsSearchParameter, summarySearchParameter);
            _serializer.Serialize(resourceNode, response.Body, pretty);
            await response.Body.FlushAsync(context.HttpContext.RequestAborted).ConfigureAwait(false);
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
    /// Writes a Firely <see cref="Resource"/> directly to the response as JSON.
    /// </summary>
    /// <remarks>
    /// This avoids the previous triple-hop (Firely serialize → Ignixa parse → Ignixa serialize)
    /// by writing Firely-produced JSON directly to the response stream.
    /// </remarks>
    private async Task WriteFirelyResourceAsync(Resource resource, HttpResponse response, bool pretty, Encoding encoding, SummaryType summaryType, string[]? elements)
    {
        using TextWriter textWriter = new StreamWriter(response.Body, encoding, bufferSize: 1024, leaveOpen: true);
        using var jsonWriter = new JsonTextWriter(textWriter);

        if (pretty)
        {
            jsonWriter.Formatting = Formatting.Indented;
        }

        await _firelySerializer.SerializeAsync(resource, jsonWriter, summaryType, elements).ConfigureAwait(false);
        await jsonWriter.FlushAsync().ConfigureAwait(false);
        await response.Body.FlushAsync().ConfigureAwait(false);
    }

    private async System.Threading.Tasks.Task<Resource> ToFirelyResourceAsync(ResourceJsonNode resourceNode)
    {
        using var stream = new MemoryStream();
        _serializer.Serialize(resourceNode, stream, pretty: false);
        stream.Position = 0;

        using var reader = new StreamReader(stream, Encoding.UTF8);
        using var jsonReader = new JsonTextReader(reader);
        return await Parser.ParseAsync<Resource>(jsonReader).ConfigureAwait(false);
    }

    private ResourceJsonNode ProjectResource(ResourceJsonNode resourceNode, IReadOnlyList<string>? elements, SummaryType summaryType)
    {
        var jsonObject = JsonNode.Parse(_serializer.Serialize(resourceNode, pretty: false)) as JsonObject
            ?? throw new InvalidOperationException("Ignixa projection requires a JSON object resource.");

        var hasElements = elements?.Any() == true;
        if (IsBundle(jsonObject) && summaryType != SummaryType.Count)
        {
            ApplyBundleProjection(jsonObject, elements, summaryType);
            AddSubsettedTag(jsonObject);
        }
        else
        {
            if (hasElements)
            {
                ApplyElementsProjection(jsonObject, elements!);
            }

            ApplySummaryProjection(jsonObject, summaryType);
            if (summaryType != SummaryType.Count)
            {
                AddSubsettedTag(jsonObject);
            }
        }

        return _serializer.Parse(jsonObject.ToJsonString());
    }

    private void ApplyBundleProjection(JsonObject jsonObject, IReadOnlyList<string>? elements, SummaryType summaryType)
    {
        if (jsonObject["entry"] is JsonArray entries)
        {
            foreach (var entry in entries.OfType<JsonObject>())
            {
                if (entry["resource"] is JsonObject entryResource)
                {
                    ApplyResourceProjection(entryResource, elements, summaryType);
                    AddSubsettedTag(entryResource);
                }
            }
        }

        var projection = new ProjectionNode();
        projection.Add("resourceType");
        projection.Add("id");
        projection.Add("meta");
        projection.Add("type");
        projection.Add("total");
        projection.Add("entry");

        FilterObject(jsonObject, projection);
    }

    private void ApplyResourceProjection(JsonObject jsonObject, IReadOnlyList<string>? elements, SummaryType summaryType)
    {
        if (elements?.Any() == true)
        {
            ApplyElementsProjection(jsonObject, elements);
        }

        ApplySummaryProjection(jsonObject, summaryType);
    }

    private void ApplyElementsProjection(JsonObject jsonObject, IReadOnlyList<string> elements)
    {
        var projection = new ProjectionNode();
        projection.Add("resourceType");
        projection.Add("id");
        projection.Add("meta");

        var resourceType = jsonObject["resourceType"]?.GetValue<string>();
        if (!string.IsNullOrWhiteSpace(resourceType))
        {
            var typeInfo = _modelInfoProvider.StructureDefinitionSummaryProvider.Provide(resourceType);
            foreach (var requiredElement in typeInfo.GetElements().Where(e => e.IsRequired).Select(e => e.ElementName))
            {
                projection.Add(requiredElement);
            }

            foreach (var element in elements.Where(e => !string.IsNullOrWhiteSpace(e)))
            {
                projection.Add(element, resourceType);
            }

            projection.AddNestedRequiredElements(typeInfo);
        }
        else
        {
            foreach (var element in elements.Where(e => !string.IsNullOrWhiteSpace(e)))
            {
                projection.Add(element, resourceType);
            }
        }

        FilterObject(jsonObject, projection);
    }

    private void ApplySummaryProjection(JsonObject jsonObject, SummaryType summaryType)
    {
        switch (summaryType)
        {
            case SummaryType.False:
                return;
            case SummaryType.Data:
                jsonObject.Remove("text");
                return;
            case SummaryType.Text:
                FilterToSummaryElements(jsonObject, includeText: true, includeRequiredElements: true, includeSummaryElements: false);
                return;
            case SummaryType.True:
                FilterToSummaryElements(jsonObject, includeText: false, includeRequiredElements: true, includeSummaryElements: true);
                return;
            case SummaryType.Count:
                FilterToCountElements(jsonObject);
                return;
        }
    }

    private static void FilterToCountElements(JsonObject jsonObject)
    {
        var projection = new ProjectionNode();
        projection.Add("resourceType");
        projection.Add("id");
        projection.Add("meta");

        if (string.Equals(jsonObject["resourceType"]?.GetValue<string>(), "Bundle", StringComparison.Ordinal))
        {
            projection.Add("type");
            projection.Add("total");
        }

        FilterObject(jsonObject, projection);
    }

    private static bool IsBundle(JsonObject jsonObject)
    {
        return string.Equals(jsonObject["resourceType"]?.GetValue<string>(), "Bundle", StringComparison.Ordinal);
    }

    private static void AddSubsettedTag(JsonObject jsonObject)
    {
        var meta = jsonObject["meta"] as JsonObject;
        if (meta == null)
        {
            meta = new JsonObject();
            jsonObject["meta"] = meta;
        }

        var tags = meta["tag"] as JsonArray;
        if (tags == null)
        {
            tags = new JsonArray();
            meta["tag"] = tags;
        }

        if (tags.OfType<JsonObject>().Any(tag =>
                string.Equals(tag["system"]?.GetValue<string>(), SubsettedTagSystem, StringComparison.Ordinal) &&
                string.Equals(tag["code"]?.GetValue<string>(), SubsettedTagCode, StringComparison.Ordinal)))
        {
            return;
        }

        tags.Add(new JsonObject
        {
            ["system"] = SubsettedTagSystem,
            ["code"] = SubsettedTagCode,
        });
    }

    private void FilterToSummaryElements(JsonObject jsonObject, bool includeText, bool includeRequiredElements, bool includeSummaryElements)
    {
        var projection = new ProjectionNode();
        projection.Add("resourceType");
        projection.Add("id");
        projection.Add("meta");

        if (includeText)
        {
            projection.Add("text");
        }

        var resourceType = jsonObject["resourceType"]?.GetValue<string>();
        if ((includeRequiredElements || includeSummaryElements) && !string.IsNullOrWhiteSpace(resourceType))
        {
            var typeInfo = _modelInfoProvider.StructureDefinitionSummaryProvider.Provide(resourceType);
            foreach (var summaryElement in typeInfo.GetElements().Where(e => (includeSummaryElements && e.InSummary) || (includeRequiredElements && e.IsRequired)).Select(e => e.ElementName))
            {
                projection.Add(summaryElement);
            }
        }

        FilterObject(jsonObject, projection);
    }

    private static void FilterObject(JsonObject jsonObject, ProjectionNode projection)
    {
        foreach (var propertyName in jsonObject.Select(property => property.Key).ToArray())
        {
            if (!projection.TryGetPropertyProjection(propertyName, out var propertyProjection))
            {
                jsonObject.Remove(propertyName);
                continue;
            }

            if (!propertyProjection.IncludeEntireElement)
            {
                FilterNode(jsonObject[propertyName], propertyProjection);
            }
        }
    }

    private static void FilterNode(JsonNode? jsonNode, ProjectionNode projection)
    {
        switch (jsonNode)
        {
            case JsonObject jsonObject:
                FilterObject(jsonObject, projection);
                break;
            case JsonArray jsonArray:
                foreach (var item in jsonArray)
                {
                    FilterNode(item, projection);
                }

                break;
        }
    }

    private string[]? GetProjectedElements(Resource resource, IEnumerable<string>? elementsSearchParameter)
    {
        if (elementsSearchParameter?.Any() != true)
        {
            return null;
        }

        var projectedElements = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var typeinfo = _modelInfoProvider.StructureDefinitionSummaryProvider.Provide(resource.TypeName);
        projectedElements.UnionWith(typeinfo.GetElements().Where(e => e.IsRequired).Select(x => x.ElementName));
        projectedElements.UnionWith(elementsSearchParameter);
        projectedElements.Add("meta");

        return projectedElements.ToArray();
    }

    private static SummaryType GetSummaryTypeOrDefault(HttpContext context)
    {
        var query = context.Request.Query[KnownQueryParameterNames.Summary].FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(query) &&
            (context.Response.StatusCode == StatusCodes.Status200OK || context.Response.StatusCode == StatusCodes.Status201Created) &&
            Enum.TryParse(query, true, out SummaryType summary))
        {
            return summary;
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            var count = context.Request.Query[KnownQueryParameterNames.Count].FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(count) &&
                int.TryParse(count, out int parsedCount) &&
                parsedCount == 0 &&
                (context.Response.StatusCode == StatusCodes.Status200OK || context.Response.StatusCode == StatusCodes.Status201Created))
            {
                return SummaryType.Count;
            }
        }

        return SummaryType.False;
    }

    private static IReadOnlyList<string>? GetElementsOrDefault(HttpContext context)
    {
        var query = context.Request.Query[KnownQueryParameterNames.Elements].FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(query) &&
            (context.Response.StatusCode == StatusCodes.Status200OK || context.Response.StatusCode == StatusCodes.Status201Created))
        {
            return query.SplitByOrSeparator();
        }

        return null;
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

    private sealed class ProjectionNode
    {
        private readonly Dictionary<string, ProjectionNode> _children = new(StringComparer.Ordinal);

        public bool IncludeEntireElement { get; private set; }

        public void Add(string elementPath, string? resourceType = null)
        {
            var path = elementPath.Trim();
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            var segments = path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var startIndex = !string.IsNullOrWhiteSpace(resourceType) &&
                segments.Length > 1 &&
                string.Equals(segments[0], resourceType, StringComparison.Ordinal)
                    ? 1
                    : 0;

            Add(segments, startIndex);
        }

        public bool TryGetPropertyProjection(string propertyName, [NotNullWhen(true)] out ProjectionNode? projection)
        {
            if (_children.TryGetValue(propertyName, out projection))
            {
                return true;
            }

            var primitiveElementName = propertyName.StartsWith('_') ? propertyName[1..] : null;
            if (primitiveElementName != null && TryGetChoiceOrConcreteProjection(primitiveElementName, out projection))
            {
                return true;
            }

            return TryGetChoiceOrConcreteProjection(propertyName, out projection);
        }

        public void AddNestedRequiredElements(IStructureDefinitionSummary structureDefinition)
        {
            AddNestedRequiredElements(structureDefinition.GetElements());
        }

        private void Add(string[] segments, int index)
        {
            if (index >= segments.Length)
            {
                IncludeEntireElement = true;
                return;
            }

            var child = GetOrAdd(segments[index]);
            child.Add(segments, index + 1);
        }

        private ProjectionNode GetOrAdd(string name)
        {
            if (!_children.TryGetValue(name, out var child))
            {
                child = new ProjectionNode();
                _children.Add(name, child);
            }

            return child;
        }

        private void AddNestedRequiredElements(IEnumerable<IElementDefinitionSummary> elements)
        {
            foreach (var child in _children.ToArray())
            {
                var childMapping = FindPropertyMapping(elements, child.Key);
                if (childMapping?.PropertyTypeMapping == null)
                {
                    continue;
                }

                child.Value.AddRequiredElements(childMapping.PropertyTypeMapping);
                child.Value.AddNestedRequiredElements(childMapping.PropertyTypeMapping.PropertyMappings);
            }
        }

        private void AddRequiredElements(ClassMapping classMapping)
        {
            foreach (var requiredElement in classMapping.PropertyMappings.Where(mapping => mapping.IsMandatoryElement).Select(mapping => mapping.Name))
            {
                Add(requiredElement);
            }
        }

        private static PropertyMapping? FindPropertyMapping(IEnumerable<IElementDefinitionSummary> elements, string elementName)
        {
            foreach (var element in elements)
            {
                if (element is PropertyMapping propertyMapping &&
                    (string.Equals(propertyMapping.Name, elementName, StringComparison.Ordinal) ||
                     (propertyMapping.Choice != ChoiceType.None && propertyMapping.DeclaringClass.FindMappedElementByChoiceName(elementName) == propertyMapping)))
                {
                    return propertyMapping;
                }
            }

            return null;
        }

        private bool TryGetChoiceOrConcreteProjection(string propertyName, [NotNullWhen(true)] out ProjectionNode? projection)
        {
            if (_children.TryGetValue(propertyName, out projection))
            {
                return true;
            }

            foreach (var child in _children)
            {
                if (IsChoicePropertyMatch(propertyName, child.Key))
                {
                    projection = child.Value;
                    return true;
                }
            }

            projection = null;
            return false;
        }

        private static bool IsChoicePropertyMatch(string propertyName, string requestedElement)
        {
            return propertyName.Length > requestedElement.Length &&
                propertyName.StartsWith(requestedElement, StringComparison.Ordinal) &&
                char.IsUpper(propertyName[requestedElement.Length]);
        }
    }
}
