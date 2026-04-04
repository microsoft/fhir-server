// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.SqlOnFhir.Evaluation;

namespace Microsoft.Health.Fhir.SqlOnFhir;

/// <summary>
/// Result of evaluating a ViewDefinition against one or more FHIR resources.
/// </summary>
public sealed class ViewDefinitionResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ViewDefinitionResult"/> class.
    /// </summary>
    /// <param name="viewDefinitionName">The name of the evaluated ViewDefinition.</param>
    /// <param name="resourceType">The FHIR resource type targeted by the ViewDefinition.</param>
    /// <param name="rows">The rows produced by evaluation.</param>
    /// <param name="schema">The column schema for the ViewDefinition.</param>
    public ViewDefinitionResult(
        string viewDefinitionName,
        string resourceType,
        IReadOnlyList<ViewDefinitionRow> rows,
        IReadOnlyList<ColumnSchema>? schema = null)
    {
        ViewDefinitionName = viewDefinitionName;
        ResourceType = resourceType;
        Rows = rows;
        Schema = schema;
    }

    /// <summary>
    /// Gets the name of the ViewDefinition that produced these results.
    /// </summary>
    public string ViewDefinitionName { get; }

    /// <summary>
    /// Gets the FHIR resource type targeted by the ViewDefinition.
    /// </summary>
    public string ResourceType { get; }

    /// <summary>
    /// Gets the rows produced by evaluating the ViewDefinition.
    /// </summary>
    public IReadOnlyList<ViewDefinitionRow> Rows { get; }

    /// <summary>
    /// Gets the column schema for the ViewDefinition, if available.
    /// </summary>
    public IReadOnlyList<ColumnSchema>? Schema { get; }
}
