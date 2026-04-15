// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Operations.ViewDefinitionRun;

/// <summary>
/// Represents a single output file from a $viewdefinition-export operation.
/// </summary>
public class ViewDefinitionExportOutput
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ViewDefinitionExportOutput"/> class.
    /// </summary>
    public ViewDefinitionExportOutput(string name, string location, string format, long rowCount = 0)
    {
        Name = name;
        Location = location;
        Format = format;
        RowCount = rowCount;
    }

    /// <summary>
    /// Gets the ViewDefinition name this output represents.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the download URL or storage location for this output file.
    /// </summary>
    public string Location { get; }

    /// <summary>
    /// Gets the format of the output file (ndjson, csv, parquet).
    /// </summary>
    public string Format { get; }

    /// <summary>
    /// Gets the number of rows in the output.
    /// </summary>
    public long RowCount { get; }
}
