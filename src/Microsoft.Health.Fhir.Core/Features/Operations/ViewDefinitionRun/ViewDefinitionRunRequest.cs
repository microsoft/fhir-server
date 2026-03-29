// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using MediatR;

namespace Microsoft.Health.Fhir.Core.Features.Operations.ViewDefinitionRun;

/// <summary>
/// MediatR request for the $viewdefinition-run operation.
/// Evaluates a ViewDefinition and returns tabular results.
/// </summary>
public class ViewDefinitionRunRequest : IRequest<ViewDefinitionRunResponse>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ViewDefinitionRunRequest"/> class.
    /// </summary>
    /// <param name="viewDefinitionJson">Inline ViewDefinition JSON (mutually exclusive with <paramref name="viewDefinitionName"/>).</param>
    /// <param name="viewDefinitionName">Name of a registered/materialized ViewDefinition (reads from SQL table).</param>
    /// <param name="format">Output format: json, csv, ndjson.</param>
    /// <param name="limit">Maximum number of rows to return.</param>
    public ViewDefinitionRunRequest(
        string viewDefinitionJson = null,
        string viewDefinitionName = null,
        string format = "json",
        int? limit = null)
    {
        ViewDefinitionJson = viewDefinitionJson;
        ViewDefinitionName = viewDefinitionName;
        Format = format;
        Limit = limit;
    }

    /// <summary>
    /// Gets the inline ViewDefinition JSON, if provided.
    /// </summary>
    public string ViewDefinitionJson { get; }

    /// <summary>
    /// Gets the name of a registered/materialized ViewDefinition, if provided.
    /// </summary>
    public string ViewDefinitionName { get; }

    /// <summary>
    /// Gets the desired output format (json, csv, ndjson).
    /// </summary>
    public string Format { get; }

    /// <summary>
    /// Gets the maximum number of rows to return.
    /// </summary>
    public int? Limit { get; }
}
