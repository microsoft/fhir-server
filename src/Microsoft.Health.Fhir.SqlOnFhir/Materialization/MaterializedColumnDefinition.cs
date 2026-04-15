// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.SqlOnFhir.Materialization;

/// <summary>
/// Represents the schema of a column in a materialized ViewDefinition SQL table.
/// </summary>
/// <param name="ColumnName">The SQL column name.</param>
/// <param name="FhirType">The FHIR type name from the ViewDefinition (e.g., "string", "dateTime").</param>
/// <param name="SqlType">The SQL Server column type (e.g., "nvarchar(max)", "datetime2(7)").</param>
/// <param name="IsCollection">Whether this column represents a collection type.</param>
public sealed record MaterializedColumnDefinition(
    string ColumnName,
    string? FhirType,
    string SqlType,
    bool IsCollection);
