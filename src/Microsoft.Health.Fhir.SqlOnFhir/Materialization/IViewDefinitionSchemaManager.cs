// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.SqlOnFhir.Materialization;

/// <summary>
/// Manages the SQL Server schema and table lifecycle for materialized ViewDefinition tables.
/// Tables are created in the <c>sqlfhir</c> schema to avoid conflicts with the core FHIR data store.
/// </summary>
public interface IViewDefinitionSchemaManager
{
    /// <summary>
    /// The SQL Server schema name used for materialized ViewDefinition tables.
    /// </summary>
    const string SchemaName = "sqlfhir";

    /// <summary>
    /// The column name added to every materialized table for tracking which resource produced each row.
    /// </summary>
    const string ResourceKeyColumnName = "_resource_key";

    /// <summary>
    /// Ensures the <c>sqlfhir</c> schema exists in the database, creating it if necessary.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task EnsureSchemaExistsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Gets the column definitions for a materialized table based on a ViewDefinition.
    /// Uses the Ignixa schema evaluator to infer column types from the ViewDefinition.
    /// </summary>
    /// <param name="viewDefinitionJson">The ViewDefinition JSON string.</param>
    /// <returns>The list of column definitions including the <c>_resource_key</c> tracking column.</returns>
    IReadOnlyList<MaterializedColumnDefinition> GetColumnDefinitions(string viewDefinitionJson);

    /// <summary>
    /// Creates a SQL table for the given ViewDefinition. The table is created in the <c>sqlfhir</c> schema
    /// with columns derived from the ViewDefinition's select expressions and a <c>_resource_key</c> tracking column.
    /// </summary>
    /// <param name="viewDefinitionJson">The ViewDefinition JSON string.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The fully qualified table name (e.g., <c>sqlfhir.patient_demographics</c>).</returns>
    Task<string> CreateTableAsync(string viewDefinitionJson, CancellationToken cancellationToken);

    /// <summary>
    /// Drops the materialized table for the given ViewDefinition name if it exists.
    /// </summary>
    /// <param name="viewDefinitionName">The ViewDefinition name (used as the table name).</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task DropTableAsync(string viewDefinitionName, CancellationToken cancellationToken);

    /// <summary>
    /// Checks whether a materialized table exists for the given ViewDefinition name.
    /// </summary>
    /// <param name="viewDefinitionName">The ViewDefinition name (used as the table name).</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns><c>true</c> if the table exists; otherwise, <c>false</c>.</returns>
    Task<bool> TableExistsAsync(string viewDefinitionName, CancellationToken cancellationToken);

    /// <summary>
    /// Generates the CREATE TABLE DDL statement for a ViewDefinition without executing it.
    /// Useful for previewing or logging the schema that would be created.
    /// </summary>
    /// <param name="viewDefinitionJson">The ViewDefinition JSON string.</param>
    /// <returns>The CREATE TABLE SQL statement.</returns>
    string GenerateCreateTableDdl(string viewDefinitionJson);
}
