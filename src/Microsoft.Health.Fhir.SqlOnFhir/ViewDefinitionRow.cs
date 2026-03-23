// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.SqlOnFhir;

/// <summary>
/// Represents a single row produced by evaluating a ViewDefinition against a FHIR resource.
/// Each key is a column name from the ViewDefinition, and each value is the extracted value.
/// </summary>
public sealed class ViewDefinitionRow
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ViewDefinitionRow"/> class.
    /// </summary>
    /// <param name="columns">The column name-value pairs for this row.</param>
    public ViewDefinitionRow(IReadOnlyDictionary<string, object?> columns)
    {
        Columns = columns;
    }

    /// <summary>
    /// Gets the column values for this row, keyed by column name.
    /// </summary>
    public IReadOnlyDictionary<string, object?> Columns { get; }

    /// <summary>
    /// Gets the value of a column by name.
    /// </summary>
    /// <param name="columnName">The column name.</param>
    /// <returns>The column value, or <c>null</c> if not present.</returns>
    public object? this[string columnName] =>
        Columns.TryGetValue(columnName, out var value) ? value : null;
}
