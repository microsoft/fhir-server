// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Nodes;
using Hl7.Fhir.ElementModel;
using Ignixa.Abstractions;
using Ignixa.Extensions.FirelySdk;
using Ignixa.Serialization;
using Ignixa.SqlOnFhir.Evaluation;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.SqlOnFhir;

/// <summary>
/// Evaluates SQL on FHIR v2 ViewDefinitions against FHIR resources by bridging
/// the Firely SDK resource model (ITypedElement) to Ignixa's IElement abstraction.
/// </summary>
public sealed class ViewDefinitionEvaluator : IViewDefinitionEvaluator
{
    private readonly SqlOnFhirEvaluator _evaluator;
    private readonly SqlOnFhirSchemaEvaluator _schemaEvaluator;
    private readonly ILogger<ViewDefinitionEvaluator> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ViewDefinitionEvaluator"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    public ViewDefinitionEvaluator(ILogger<ViewDefinitionEvaluator> logger)
    {
        _evaluator = new SqlOnFhirEvaluator();
        _schemaEvaluator = new SqlOnFhirSchemaEvaluator();
        _logger = logger;
    }

    /// <inheritdoc />
    public ViewDefinitionResult Evaluate(string viewDefinitionJson, ResourceElement resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        return Evaluate(viewDefinitionJson, resource.Instance);
    }

    /// <inheritdoc />
    public ViewDefinitionResult Evaluate(string viewDefinitionJson, ITypedElement typedElement)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(viewDefinitionJson);
        ArgumentNullException.ThrowIfNull(typedElement);

        ISourceNavigator viewDefNode = ParseViewDefinitionJson(viewDefinitionJson);
        IElement ignixaElement = typedElement.ToIgnixaElement();

        _logger.LogDebug(
            "Evaluating ViewDefinition against {ResourceType}/{ResourceId}",
            typedElement.InstanceType,
            typedElement.Name);

        var rawRows = _evaluator.Evaluate(viewDefNode, ignixaElement);

        List<ViewDefinitionRow> rows = ConvertRows(rawRows);

        (string name, string resourceType) = ExtractViewDefinitionMetadata(viewDefinitionJson);

        _logger.LogDebug(
            "ViewDefinition '{ViewDefName}' produced {RowCount} row(s) for {ResourceType}",
            name,
            rows.Count,
            resourceType);

        return new ViewDefinitionResult(name, resourceType, rows);
    }

    /// <inheritdoc />
    public ViewDefinitionResult EvaluateMany(string viewDefinitionJson, IEnumerable<ResourceElement> resources)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(viewDefinitionJson);
        ArgumentNullException.ThrowIfNull(resources);

        ISourceNavigator viewDefNode = ParseViewDefinitionJson(viewDefinitionJson);
        var allRows = new List<ViewDefinitionRow>();

        foreach (ResourceElement resource in resources)
        {
            IElement ignixaElement = resource.Instance.ToIgnixaElement();
            var rawRows = _evaluator.Evaluate(viewDefNode, ignixaElement);
            allRows.AddRange(ConvertRows(rawRows));
        }

        (string name, string resourceType) = ExtractViewDefinitionMetadata(viewDefinitionJson);

        _logger.LogInformation(
            "ViewDefinition '{ViewDefName}' produced {RowCount} row(s) across multiple {ResourceType} resources",
            name,
            allRows.Count,
            resourceType);

        return new ViewDefinitionResult(name, resourceType, allRows);
    }

    private static List<ViewDefinitionRow> ConvertRows(IEnumerable<Dictionary<string, object?>> rawRows)
    {
        return rawRows
            .Select(dict => new ViewDefinitionRow(
                dict.ToDictionary(
                    kvp => kvp.Key,
                    kvp => (object?)kvp.Value)))
            .ToList();
    }

    private static ISourceNavigator ParseViewDefinitionJson(string viewDefinitionJson)
    {
        return JsonSourceNodeFactory.Parse(viewDefinitionJson).ToSourceNavigator();
    }

    private static (string Name, string ResourceType) ExtractViewDefinitionMetadata(string viewDefinitionJson)
    {
        using JsonDocument doc = JsonDocument.Parse(viewDefinitionJson);
        JsonElement root = doc.RootElement;

        string name = root.TryGetProperty("name", out JsonElement nameElement)
            ? nameElement.GetString() ?? "unknown"
            : "unknown";

        string resourceType = root.TryGetProperty("resource", out JsonElement resourceElement)
            ? resourceElement.GetString() ?? "unknown"
            : "unknown";

        return (name, resourceType);
    }
}
