// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;

namespace Microsoft.Health.Fhir.Core.Features.Operations.ViewDefinitionRun;

/// <summary>
/// Response for the $viewdefinition-export operation.
/// </summary>
public class ViewDefinitionExportResponse
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ViewDefinitionExportResponse"/> class.
    /// </summary>
    public ViewDefinitionExportResponse(
        bool isComplete,
        string exportId = null,
        string statusUrl = null,
        IReadOnlyList<ViewDefinitionExportOutput> outputs = null)
    {
        IsComplete = isComplete;
        ExportId = exportId;
        StatusUrl = statusUrl;
        Outputs = outputs ?? System.Array.Empty<ViewDefinitionExportOutput>();
    }

    /// <summary>
    /// Gets a value indicating whether the export is already complete (fast path for materialized data).
    /// </summary>
    public bool IsComplete { get; }

    /// <summary>
    /// Gets the export job ID (for async path).
    /// </summary>
    public string ExportId { get; }

    /// <summary>
    /// Gets the status polling URL (for async path).
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1056:URI-like properties should not be strings", Justification = "Status URL is a relative path segment, not a full URI")]
    public string StatusUrl { get; }

    /// <summary>
    /// Gets the output file locations (for fast path when already materialized).
    /// </summary>
    public IReadOnlyList<ViewDefinitionExportOutput> Outputs { get; }
}
