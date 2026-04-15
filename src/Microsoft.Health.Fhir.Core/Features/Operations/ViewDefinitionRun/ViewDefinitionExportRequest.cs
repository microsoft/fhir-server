// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using MediatR;

namespace Microsoft.Health.Fhir.Core.Features.Operations.ViewDefinitionRun;

/// <summary>
/// MediatR request for the $viewdefinition-export operation (async bulk export of ViewDefinition results).
/// </summary>
public class ViewDefinitionExportRequest : IRequest<ViewDefinitionExportResponse>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ViewDefinitionExportRequest"/> class.
    /// </summary>
    public ViewDefinitionExportRequest(
        string viewDefinitionJson = null,
        string viewDefinitionName = null,
        string format = "ndjson")
    {
        ViewDefinitionJson = viewDefinitionJson;
        ViewDefinitionName = viewDefinitionName;
        Format = format;
    }

    /// <summary>
    /// Gets the inline ViewDefinition JSON, if provided.
    /// </summary>
    public string ViewDefinitionJson { get; }

    /// <summary>
    /// Gets the name of a registered ViewDefinition, if provided.
    /// </summary>
    public string ViewDefinitionName { get; }

    /// <summary>
    /// Gets the desired output format (ndjson, csv, parquet).
    /// </summary>
    public string Format { get; }
}
