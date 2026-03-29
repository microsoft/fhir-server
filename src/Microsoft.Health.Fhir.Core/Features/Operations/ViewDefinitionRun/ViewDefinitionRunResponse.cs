// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;

namespace Microsoft.Health.Fhir.Core.Features.Operations.ViewDefinitionRun;

/// <summary>
/// Response for the $viewdefinition-run operation.
/// Contains the tabular results as a list of row dictionaries, plus the formatted output string.
/// </summary>
public class ViewDefinitionRunResponse
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ViewDefinitionRunResponse"/> class.
    /// </summary>
    /// <param name="formattedOutput">The result data formatted as the requested type (JSON, CSV, etc.).</param>
    /// <param name="contentType">The MIME content type of the formatted output.</param>
    /// <param name="rowCount">The number of rows in the result.</param>
    public ViewDefinitionRunResponse(string formattedOutput, string contentType, int rowCount)
    {
        FormattedOutput = formattedOutput;
        ContentType = contentType;
        RowCount = rowCount;
    }

    /// <summary>
    /// Gets the formatted output string (JSON array, CSV, or NDJSON).
    /// </summary>
    public string FormattedOutput { get; }

    /// <summary>
    /// Gets the MIME content type for the response.
    /// </summary>
    public string ContentType { get; }

    /// <summary>
    /// Gets the number of rows in the result.
    /// </summary>
    public int RowCount { get; }
}
