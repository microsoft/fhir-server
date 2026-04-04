// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;

namespace Microsoft.Health.Fhir.SqlOnFhir.Materialization;

/// <summary>
/// Handles incremental materialization of ViewDefinition results into SQL Server tables.
/// Uses an atomic delete-then-insert pattern to keep materialized rows current.
/// </summary>
public sealed class SqlServerViewDefinitionMaterializer : IViewDefinitionMaterializer
{
    private readonly IViewDefinitionEvaluator _evaluator;
    private readonly IViewDefinitionSchemaManager _schemaManager;
    private readonly ISqlRetryService _sqlRetryService;
    private readonly ILogger<SqlServerViewDefinitionMaterializer> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqlServerViewDefinitionMaterializer"/> class.
    /// </summary>
    /// <param name="evaluator">The ViewDefinition evaluator for re-evaluating resources.</param>
    /// <param name="schemaManager">The schema manager for column definitions.</param>
    /// <param name="sqlRetryService">The SQL retry service for resilient database access.</param>
    /// <param name="logger">The logger instance.</param>
    public SqlServerViewDefinitionMaterializer(
        IViewDefinitionEvaluator evaluator,
        IViewDefinitionSchemaManager schemaManager,
        ISqlRetryService sqlRetryService,
        ILogger<SqlServerViewDefinitionMaterializer> logger)
    {
        _evaluator = evaluator;
        _schemaManager = schemaManager;
        _sqlRetryService = sqlRetryService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<int> UpsertResourceAsync(
        string viewDefinitionJson,
        string viewDefinitionName,
        ResourceElement resource,
        string resourceKey,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(viewDefinitionJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(viewDefinitionName);
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceKey);

        // Evaluate the ViewDefinition against the resource
        ViewDefinitionResult result = _evaluator.Evaluate(viewDefinitionJson, resource);

        // Get column definitions for building INSERT statements
        IReadOnlyList<MaterializedColumnDefinition> columnDefs = _schemaManager.GetColumnDefinitions(viewDefinitionJson);

        string qualifiedTable = SqlServerViewDefinitionSchemaManager.GetQualifiedTableName(viewDefinitionName);

        if (result.Rows.Count == 0)
        {
            // Resource doesn't match the ViewDefinition filter — just delete any existing rows
            int deleted = await DeleteResourceAsync(viewDefinitionName, resourceKey, cancellationToken);
            _logger.LogDebug(
                "Resource '{ResourceKey}' produced zero rows for '{ViewDef}'. Deleted {DeletedCount} existing row(s)",
                resourceKey,
                viewDefinitionName,
                deleted);
            return 0;
        }

        // Build the atomic DELETE + INSERT SQL batch
        string sql = BuildUpsertSql(qualifiedTable, columnDefs, result.Rows, resourceKey);

        // CA2100: Dynamic SQL is safe — table/column names are validated via regex in SchemaManager
        #pragma warning disable CA2100
        using var cmd = new SqlCommand(sql);
        #pragma warning restore CA2100
        AddRowParameters(cmd, columnDefs, result.Rows, resourceKey);

        await cmd.ExecuteNonQueryAsync(
            _sqlRetryService,
            _logger,
            cancellationToken,
            logMessage: $"UpsertResource:{viewDefinitionName}/{resourceKey}");

        _logger.LogDebug(
            "Upserted {RowCount} row(s) for resource '{ResourceKey}' in '{ViewDef}'",
            result.Rows.Count,
            resourceKey,
            viewDefinitionName);

        return result.Rows.Count;
    }

    /// <inheritdoc />
    public async Task<int> DeleteResourceAsync(
        string viewDefinitionName,
        string resourceKey,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(viewDefinitionName);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceKey);

        string qualifiedTable = SqlServerViewDefinitionSchemaManager.GetQualifiedTableName(viewDefinitionName);

        string sql = $"""
            DELETE FROM {qualifiedTable}
            WHERE [{IViewDefinitionSchemaManager.ResourceKeyColumnName}] = @ResourceKey
            """;

        // CA2100: Dynamic SQL is safe — table name is bracket-quoted and validated via regex in SchemaManager
        #pragma warning disable CA2100
        using var cmd = new SqlCommand(sql);
        #pragma warning restore CA2100
        cmd.Parameters.AddWithValue("@ResourceKey", resourceKey);

        int deletedCount = 0;
        await _sqlRetryService.ExecuteSql(
            cmd,
            async (sqlCmd, ct) =>
            {
                deletedCount = await sqlCmd.ExecuteNonQueryAsync(ct);
            },
            _logger,
            $"DeleteResource:{viewDefinitionName}/{resourceKey}",
            cancellationToken);

        _logger.LogDebug(
            "Deleted {DeletedCount} row(s) for resource '{ResourceKey}' from '{ViewDef}'",
            deletedCount,
            resourceKey,
            viewDefinitionName);

        return deletedCount;
    }

    /// <summary>
    /// Builds a SQL batch that atomically deletes existing rows and inserts new ones for a resource.
    /// </summary>
    internal static string BuildUpsertSql(
        string qualifiedTableName,
        IReadOnlyList<MaterializedColumnDefinition> columnDefs,
        IReadOnlyList<ViewDefinitionRow> rows,
        string resourceKey)
    {
        var sb = new StringBuilder();

        // Delete existing rows for this resource
        sb.AppendLine($"DELETE FROM {qualifiedTableName}");
        sb.AppendLine($"WHERE [{IViewDefinitionSchemaManager.ResourceKeyColumnName}] = @ResourceKey;");
        sb.AppendLine();

        // Build column list (all columns from the schema)
        string columnList = string.Join(", ", columnDefs.Select(c => $"[{c.ColumnName}]"));

        // Insert new rows
        for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            string paramList = string.Join(", ", columnDefs.Select(c =>
            {
                if (c.ColumnName == IViewDefinitionSchemaManager.ResourceKeyColumnName)
                {
                    return "@ResourceKey";
                }

                return $"@r{rowIndex}_{c.ColumnName}";
            }));

            sb.AppendLine($"INSERT INTO {qualifiedTableName} ({columnList})");
            sb.AppendLine($"VALUES ({paramList});");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Adds SQL parameters for all rows to the command.
    /// </summary>
    internal static void AddRowParameters(
        SqlCommand cmd,
        IReadOnlyList<MaterializedColumnDefinition> columnDefs,
        IReadOnlyList<ViewDefinitionRow> rows,
        string resourceKey)
    {
        cmd.Parameters.AddWithValue("@ResourceKey", resourceKey);

        for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            ViewDefinitionRow row = rows[rowIndex];

            foreach (MaterializedColumnDefinition colDef in columnDefs)
            {
                if (colDef.ColumnName == IViewDefinitionSchemaManager.ResourceKeyColumnName)
                {
                    continue; // Handled by @ResourceKey
                }

                string paramName = $"@r{rowIndex}_{colDef.ColumnName}";
                object? value = row[colDef.ColumnName];

                cmd.Parameters.AddWithValue(paramName, ConvertToSqlValue(value, colDef));
            }
        }
    }

    /// <summary>
    /// Converts a FHIR value from the ViewDefinition evaluator to a SQL-compatible value.
    /// </summary>
    private static object ConvertToSqlValue(object? value, MaterializedColumnDefinition colDef)
    {
        if (value is null)
        {
            return DBNull.Value;
        }

        // For collection types, serialize to JSON string
        if (colDef.IsCollection)
        {
            return System.Text.Json.JsonSerializer.Serialize(value);
        }

        // Convert based on the target SQL type
        return colDef.SqlType switch
        {
            "bit" => Convert.ToBoolean(value) ? 1 : 0,
            "int" => Convert.ToInt32(value),
            "bigint" => Convert.ToInt64(value),
            "decimal(18, 9)" => Convert.ToDecimal(value),
            "date" or "datetime2(7)" or "time(7)" => ConvertToDateTimeValue(value),
            _ => value.ToString() ?? string.Empty,
        };
    }

    /// <summary>
    /// Converts date/time values from FHIR format to SQL-compatible values.
    /// </summary>
    private static object ConvertToDateTimeValue(object value)
    {
        if (value is DateTimeOffset dto)
        {
            return dto.UtcDateTime;
        }

        if (value is DateTime dt)
        {
            return dt;
        }

        string? strValue = value.ToString();
        if (strValue is not null && DateTimeOffset.TryParse(strValue, out DateTimeOffset parsed))
        {
            return parsed.UtcDateTime;
        }

        // Return as string if parsing fails — SQL Server will attempt conversion
        return strValue ?? string.Empty;
    }
}
