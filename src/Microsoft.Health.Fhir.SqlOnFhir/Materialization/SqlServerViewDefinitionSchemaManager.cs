// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.RegularExpressions;
using Ignixa.Serialization;
using Ignixa.SqlOnFhir.Evaluation;
using Ignixa.SqlOnFhir.Parsing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;

namespace Microsoft.Health.Fhir.SqlOnFhir.Materialization;

/// <summary>
/// Manages the SQL Server schema and table lifecycle for materialized ViewDefinition tables.
/// Creates tables in the <c>sqlfhir</c> schema with columns derived from ViewDefinition metadata.
/// </summary>
public sealed class SqlServerViewDefinitionSchemaManager : IViewDefinitionSchemaManager
{
    /// <summary>
    /// Regex for valid SQL identifiers (alphanumeric and underscores only).
    /// </summary>
    private static readonly Regex ValidIdentifierPattern = new(@"^[a-zA-Z_][a-zA-Z0-9_]*$", RegexOptions.Compiled);

    private readonly ISqlRetryService _sqlRetryService;
    private readonly SqlOnFhirSchemaEvaluator _schemaEvaluator;
    private readonly ILogger<SqlServerViewDefinitionSchemaManager> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqlServerViewDefinitionSchemaManager"/> class.
    /// </summary>
    /// <param name="sqlRetryService">The SQL retry service for resilient database access.</param>
    /// <param name="logger">The logger instance.</param>
    public SqlServerViewDefinitionSchemaManager(
        ISqlRetryService sqlRetryService,
        ILogger<SqlServerViewDefinitionSchemaManager> logger)
    {
        _sqlRetryService = sqlRetryService;
        _schemaEvaluator = new SqlOnFhirSchemaEvaluator();
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task EnsureSchemaExistsAsync(CancellationToken cancellationToken)
    {
        const string sql = $"""
            IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = '{IViewDefinitionSchemaManager.SchemaName}')
            BEGIN
                EXEC('CREATE SCHEMA [{IViewDefinitionSchemaManager.SchemaName}]')
            END
            """;

        using var cmd = new SqlCommand(sql);
        await cmd.ExecuteNonQueryAsync(_sqlRetryService, _logger, cancellationToken, logMessage: "EnsureSchemaExists");

        _logger.LogInformation("Ensured SQL schema '{SchemaName}' exists", IViewDefinitionSchemaManager.SchemaName);
    }

    /// <inheritdoc />
    public IReadOnlyList<MaterializedColumnDefinition> GetColumnDefinitions(string viewDefinitionJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(viewDefinitionJson);

        var viewDefNode = JsonSourceNodeFactory.Parse(viewDefinitionJson).ToSourceNavigator();
        var expression = ViewDefinitionExpressionParser.Parse(viewDefNode);

        IReadOnlyList<ColumnSchema> igniaxSchema = _schemaEvaluator.GetSchema(expression);

        var columns = new List<MaterializedColumnDefinition>();

        // Add the resource key tracking column first
        columns.Add(new MaterializedColumnDefinition(
            IViewDefinitionSchemaManager.ResourceKeyColumnName,
            FhirType: null,
            SqlType: "nvarchar(128)",
            IsCollection: false));

        // Add columns from the ViewDefinition schema
        foreach (ColumnSchema col in igniaxSchema)
        {
            string sqlType = col.Collection
                ? "nvarchar(max)" // Collections are stored as JSON arrays
                : FhirTypeToSqlTypeMap.GetSqlType(col.Type);

            columns.Add(new MaterializedColumnDefinition(
                col.Name,
                col.Type,
                sqlType,
                col.Collection));
        }

        return columns;
    }

    /// <inheritdoc />
    public async Task<string> CreateTableAsync(string viewDefinitionJson, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(viewDefinitionJson);

        string ddl = GenerateCreateTableDdl(viewDefinitionJson);
        string tableName = ExtractViewDefinitionName(viewDefinitionJson);
        string qualifiedName = GetQualifiedTableName(tableName);

        await EnsureSchemaExistsAsync(cancellationToken);

        // CA2100: DDL requires dynamic SQL; identifiers are validated via ValidateIdentifier regex
        #pragma warning disable CA2100
        using var cmd = new SqlCommand(ddl);
        #pragma warning restore CA2100
        await cmd.ExecuteNonQueryAsync(_sqlRetryService, _logger, cancellationToken, logMessage: $"CreateTable:{qualifiedName}");

        _logger.LogInformation("Created materialized table '{TableName}'", qualifiedName);

        return qualifiedName;
    }

    /// <inheritdoc />
    public async Task DropTableAsync(string viewDefinitionName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(viewDefinitionName);
        ValidateIdentifier(viewDefinitionName);

        string qualifiedName = GetQualifiedTableName(viewDefinitionName);
        string sql = $"DROP TABLE IF EXISTS {qualifiedName}";

        // CA2100: DDL requires dynamic SQL; identifiers are validated via ValidateIdentifier regex
        #pragma warning disable CA2100
        using var cmd = new SqlCommand(sql);
        #pragma warning restore CA2100
        await cmd.ExecuteNonQueryAsync(_sqlRetryService, _logger, cancellationToken, logMessage: $"DropTable:{qualifiedName}");

        _logger.LogInformation("Dropped materialized table '{TableName}'", qualifiedName);
    }

    /// <inheritdoc />
    public async Task<bool> TableExistsAsync(string viewDefinitionName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(viewDefinitionName);
        ValidateIdentifier(viewDefinitionName);

        const string sql = """
            SELECT CASE WHEN EXISTS (
                SELECT 1 FROM INFORMATION_SCHEMA.TABLES
                WHERE TABLE_SCHEMA = @SchemaName AND TABLE_NAME = @TableName
            ) THEN 1 ELSE 0 END
            """;

        using var cmd = new SqlCommand(sql);
        cmd.Parameters.AddWithValue("@SchemaName", IViewDefinitionSchemaManager.SchemaName);
        cmd.Parameters.AddWithValue("@TableName", viewDefinitionName);

        object? result = await cmd.ExecuteScalarAsync(_sqlRetryService, _logger, cancellationToken, logMessage: $"TableExists:{viewDefinitionName}");

        return Convert.ToInt32(result) == 1;
    }

    /// <inheritdoc />
    public string GenerateCreateTableDdl(string viewDefinitionJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(viewDefinitionJson);

        string tableName = ExtractViewDefinitionName(viewDefinitionJson);
        ValidateIdentifier(tableName);

        IReadOnlyList<MaterializedColumnDefinition> columns = GetColumnDefinitions(viewDefinitionJson);
        string qualifiedName = GetQualifiedTableName(tableName);

        var sb = new StringBuilder();
        sb.AppendLine($"CREATE TABLE {qualifiedName}");
        sb.AppendLine("(");

        for (int i = 0; i < columns.Count; i++)
        {
            MaterializedColumnDefinition col = columns[i];
            string columnName = SanitizeColumnName(col.ColumnName);
            string nullable = col.ColumnName == IViewDefinitionSchemaManager.ResourceKeyColumnName
                ? "NOT NULL"
                : "NULL";

            sb.Append($"    [{columnName}] {col.SqlType} {nullable}");

            if (i < columns.Count - 1)
            {
                sb.AppendLine(",");
            }
            else
            {
                sb.AppendLine();
            }
        }

        sb.AppendLine(")");

        // Add a non-clustered index on _resource_key for efficient incremental updates
        sb.AppendLine();
        sb.AppendLine($"CREATE NONCLUSTERED INDEX [IX_{tableName}_{IViewDefinitionSchemaManager.ResourceKeyColumnName}]");
        sb.AppendLine($"    ON {qualifiedName} ([{IViewDefinitionSchemaManager.ResourceKeyColumnName}])");

        return sb.ToString();
    }

    /// <summary>
    /// Gets the fully qualified table name in the <c>sqlfhir</c> schema.
    /// </summary>
    internal static string GetQualifiedTableName(string viewDefinitionName)
    {
        return $"[{IViewDefinitionSchemaManager.SchemaName}].[{viewDefinitionName}]";
    }

    /// <summary>
    /// Extracts the ViewDefinition name from a ViewDefinition JSON string.
    /// </summary>
    internal static string ExtractViewDefinitionName(string viewDefinitionJson)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(viewDefinitionJson);
        var root = doc.RootElement;

        if (root.TryGetProperty("name", out var nameElement) && nameElement.GetString() is string name)
        {
            return name;
        }

        throw new ArgumentException("ViewDefinition JSON must contain a 'name' property.", nameof(viewDefinitionJson));
    }

    /// <summary>
    /// Sanitizes a column name to be a valid SQL identifier.
    /// Replaces invalid characters with underscores.
    /// </summary>
    private static string SanitizeColumnName(string columnName)
    {
        // If it's already valid, return as-is
        if (ValidIdentifierPattern.IsMatch(columnName))
        {
            return columnName;
        }

        // Replace non-alphanumeric/underscore characters
        return Regex.Replace(columnName, @"[^a-zA-Z0-9_]", "_");
    }

    /// <summary>
    /// Validates that an identifier is safe for use in SQL statements.
    /// </summary>
    private static void ValidateIdentifier(string identifier)
    {
        if (!ValidIdentifierPattern.IsMatch(identifier))
        {
            throw new ArgumentException(
                $"Invalid SQL identifier: '{identifier}'. Only alphanumeric characters and underscores are allowed.",
                nameof(identifier));
        }
    }
}
