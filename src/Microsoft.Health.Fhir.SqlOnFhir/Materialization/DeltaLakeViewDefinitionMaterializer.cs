// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;
using Apache.Arrow;
using Apache.Arrow.Types;
using DeltaLake.Interfaces;
using DeltaLake.Table;
using Ignixa.Serialization;
using Ignixa.SqlOnFhir.Evaluation;
using Ignixa.SqlOnFhir.Parsing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.SqlOnFhir.Materialization;

/// <summary>
/// Materializes ViewDefinition results as Delta Lake tables for Microsoft Fabric / OneLake.
/// Uses ACID MERGE operations on <c>_resource_key</c> for proper upsert semantics, and
/// DELETE operations for resource deletions — unlike the append-only Parquet materializer.
/// <para>
/// Delta tables are stored at <c>{storageUri}/{container}/{viewDefinitionName}/</c> and are
/// directly queryable via Fabric's SQL Analytics Endpoint, Power BI DirectQuery, and Spark.
/// </para>
/// </summary>
public sealed class DeltaLakeViewDefinitionMaterializer : IViewDefinitionMaterializer, IDisposable
{
    private static readonly string[] AzureStorageScopes = new[] { "https://storage.azure.com/.default" };

    private readonly IViewDefinitionEvaluator _evaluator;
    private readonly IEngine _engine;
    private readonly SqlOnFhirMaterializationConfiguration _config;
    private readonly SqlOnFhirSchemaEvaluator _schemaEvaluator;
    private readonly ILogger<DeltaLakeViewDefinitionMaterializer> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _tableLocks = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="DeltaLakeViewDefinitionMaterializer"/> class.
    /// </summary>
    /// <param name="evaluator">The ViewDefinition evaluator for re-evaluating resources.</param>
    /// <param name="engine">The Delta Lake engine for table operations.</param>
    /// <param name="config">The materialization configuration.</param>
    /// <param name="logger">The logger instance.</param>
    public DeltaLakeViewDefinitionMaterializer(
        IViewDefinitionEvaluator evaluator,
        IEngine engine,
        IOptions<SqlOnFhirMaterializationConfiguration> config,
        ILogger<DeltaLakeViewDefinitionMaterializer> logger)
    {
        _evaluator = evaluator;
        _engine = engine;
        _config = config.Value;
        _schemaEvaluator = new SqlOnFhirSchemaEvaluator();
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<bool> EnsureStorageAsync(string viewDefinitionJson, string viewDefinitionName, CancellationToken cancellationToken)
    {
        // Delta Lake tables are created on-the-fly by LoadOrCreateTableAsync during the first write.
        // No upfront provisioning is needed.
        _logger.LogDebug("Delta Lake storage for '{ViewDefName}' will be created on first write", viewDefinitionName);
        return Task.FromResult(false);
    }

    /// <inheritdoc />
    public async Task<bool> StorageExistsAsync(string viewDefinitionName, CancellationToken cancellationToken)
    {
        string tableUri = GetTableUri(viewDefinitionName);
        try
        {
            using ITable table = await _engine.LoadTableAsync(
                new TableOptions { TableLocation = tableUri, StorageOptions = GetStorageOptions() },
                cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <inheritdoc />
    public Task CleanupStorageAsync(string viewDefinitionName, CancellationToken cancellationToken)
    {
        // Delta Lake table deletion requires removing the storage directory.
        // This is a best-effort operation — the table directory may not exist.
        _logger.LogInformation(
            "Delta Lake table cleanup for '{ViewDefName}' — table directory at '{TableUri}' should be removed manually or via storage lifecycle policies",
            viewDefinitionName,
            GetTableUri(viewDefinitionName));
        return Task.CompletedTask;
    }

    /// <summary>
    /// Creates a Delta Lake checkpoint for the specified ViewDefinition table.
    /// This writes the <c>_last_checkpoint</c> file that Fabric requires to recognize
    /// tables with many transaction log entries.
    /// </summary>
    /// <param name="viewDefinitionName">The ViewDefinition name (Delta table name).</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public async Task CheckpointAsync(string viewDefinitionName, CancellationToken cancellationToken)
    {
        string tableUri = GetTableUri(viewDefinitionName);

        try
        {
            using ITable table = await _engine.LoadTableAsync(
                new TableOptions { TableLocation = tableUri, StorageOptions = GetStorageOptions() },
                cancellationToken);

            await table.CheckpointAsync(cancellationToken);

            _logger.LogInformation(
                "Delta Lake checkpoint created for '{ViewDefName}' at version {Version}",
                viewDefinitionName,
                table.Version());
        }
        catch (Exception ex)
        {
            const string message =
                "Failed to create Delta Lake checkpoint for '{ViewDefName}'. "
                + "Fabric may show the table as 'Unidentified' until a checkpoint is created";
            _logger.LogWarning(ex, message, viewDefinitionName);
        }
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

        ViewDefinitionResult result = _evaluator.Evaluate(viewDefinitionJson, resource);

        if (result.Rows.Count == 0)
        {
            // Resource doesn't match ViewDefinition filter — delete any existing rows
            await DeleteResourceAsync(viewDefinitionName, resourceKey, cancellationToken);

            _logger.LogDebug(
                "Resource '{ResourceKey}' produced zero rows for Delta Lake ViewDef '{ViewDef}'",
                resourceKey,
                viewDefinitionName);
            return 0;
        }

        IReadOnlyList<ColumnSchema> columns = GetColumnSchema(viewDefinitionJson);
        Apache.Arrow.Schema arrowSchema = BuildArrowSchema(columns);
        using RecordBatch recordBatch = BuildRecordBatch(arrowSchema, columns, result.Rows, resourceKey);

        string tableUri = GetTableUri(viewDefinitionName);
        SemaphoreSlim tableLock = _tableLocks.GetOrAdd(viewDefinitionName, _ => new SemaphoreSlim(1, 1));

        await tableLock.WaitAsync(cancellationToken);
        try
        {
            using ITable table = await LoadOrCreateTableAsync(tableUri, arrowSchema, cancellationToken);

            string mergeSql = BuildMergeSql(viewDefinitionName, columns);

            await table.MergeAsync(mergeSql, [recordBatch], arrowSchema, cancellationToken);

            _logger.LogDebug(
                "Delta Lake MERGE: {RowCount} row(s) for resource '{ResourceKey}' in '{ViewDef}'",
                result.Rows.Count,
                resourceKey,
                viewDefinitionName);
        }
        finally
        {
            tableLock.Release();
        }

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

        string tableUri = GetTableUri(viewDefinitionName);
        SemaphoreSlim tableLock = _tableLocks.GetOrAdd(viewDefinitionName, _ => new SemaphoreSlim(1, 1));

        await tableLock.WaitAsync(cancellationToken);
        try
        {
            ITable table;
            try
            {
                table = await _engine.LoadTableAsync(
                    new TableOptions { TableLocation = tableUri, StorageOptions = GetStorageOptions() },
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(
                    ex,
                    "Delta Lake table '{ViewDefName}' does not exist yet; skipping delete for '{ResourceKey}'",
                    viewDefinitionName,
                    resourceKey);
                return 0;
            }

            using (table)
            {
                // Escape single quotes in resourceKey for SQL predicate safety
                string escapedKey = resourceKey.Replace("'", "''", StringComparison.Ordinal);
                string predicate = $"{IViewDefinitionSchemaManager.ResourceKeyColumnName} = '{escapedKey}'";

                await table.DeleteAsync(predicate, cancellationToken);

                _logger.LogDebug(
                    "Delta Lake DELETE for resource '{ResourceKey}' from '{ViewDefName}'",
                    resourceKey,
                    viewDefinitionName);
            }
        }
        finally
        {
            tableLock.Release();
        }

        // Delta Lake delete doesn't return affected row count; return 1 as an estimate
        return 1;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (SemaphoreSlim semaphore in _tableLocks.Values)
        {
            semaphore.Dispose();
        }

        _tableLocks.Clear();
    }

    /// <summary>
    /// Loads an existing Delta table or creates a new one if it doesn't exist.
    /// </summary>
    internal async Task<ITable> LoadOrCreateTableAsync(
        string tableUri,
        Apache.Arrow.Schema schema,
        CancellationToken cancellationToken)
    {
        Dictionary<string, string> storageOptions = GetStorageOptions();

        try
        {
            return await _engine.LoadTableAsync(
                new TableOptions { TableLocation = tableUri, StorageOptions = storageOptions },
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogInformation(
                ex,
                "Delta table at '{TableUri}' not found, creating new table",
                tableUri);

            return await _engine.CreateTableAsync(
                new TableCreateOptions(tableUri, schema) { StorageOptions = storageOptions },
                cancellationToken);
        }
    }

    /// <summary>
    /// Builds the SQL MERGE statement for upserting rows by <c>_resource_key</c>.
    /// </summary>
    internal static string BuildMergeSql(string viewDefinitionName, IReadOnlyList<ColumnSchema> columns)
    {
        string allColumns = string.Join(
            ", ",
            new[] { IViewDefinitionSchemaManager.ResourceKeyColumnName }
                .Concat(columns.Select(c => c.Name)));

        string updateSet = string.Join(
            ", ",
            columns.Select(c => $"target.{c.Name} = source.{c.Name}"));

        // Also update _resource_key in case it appears in the update set
        string fullUpdateSet = $"target.{IViewDefinitionSchemaManager.ResourceKeyColumnName} = source.{IViewDefinitionSchemaManager.ResourceKeyColumnName}" +
            (updateSet.Length > 0 ? $", {updateSet}" : string.Empty);

        return $"""
            MERGE INTO {viewDefinitionName} AS target
            USING source AS source
            ON target.{IViewDefinitionSchemaManager.ResourceKeyColumnName} = source.{IViewDefinitionSchemaManager.ResourceKeyColumnName}
            WHEN MATCHED THEN UPDATE SET {fullUpdateSet}
            WHEN NOT MATCHED THEN INSERT ({allColumns}) VALUES ({string.Join(", ", allColumns.Split(", ").Select(c => $"source.{c}"))})
            """;
    }

    /// <summary>
    /// Builds an Apache Arrow schema from ViewDefinition column definitions, prepending <c>_resource_key</c>.
    /// </summary>
    internal static Apache.Arrow.Schema BuildArrowSchema(IReadOnlyList<ColumnSchema> columns)
    {
        var builder = new Apache.Arrow.Schema.Builder();

        builder.Field(fb =>
        {
            fb.Name(IViewDefinitionSchemaManager.ResourceKeyColumnName);
            fb.DataType(StringType.Default);
            fb.Nullable(false);
        });

        foreach (ColumnSchema col in columns)
        {
            IArrowType arrowType = MapFhirTypeToArrowType(col.Type);
            builder.Field(fb =>
            {
                fb.Name(col.Name);
                fb.DataType(arrowType);
                fb.Nullable(true);
            });
        }

        return builder.Build();
    }

    /// <summary>
    /// Builds an Apache Arrow RecordBatch from ViewDefinition rows.
    /// </summary>
    internal static RecordBatch BuildRecordBatch(
        Apache.Arrow.Schema schema,
        IReadOnlyList<ColumnSchema> columns,
        IReadOnlyList<ViewDefinitionRow> rows,
        string resourceKey)
    {
        int rowCount = rows.Count;
        var arrays = new List<IArrowArray>();

        // _resource_key column
        var resourceKeyBuilder = new StringArray.Builder();
        for (int i = 0; i < rowCount; i++)
        {
            resourceKeyBuilder.Append(resourceKey);
        }

        arrays.Add(resourceKeyBuilder.Build());

        // ViewDefinition columns
        foreach (ColumnSchema col in columns)
        {
            IArrowArray array = BuildColumnArray(col, rows);
            arrays.Add(array);
        }

        return new RecordBatch(schema, arrays, rowCount);
    }

    private static IArrowArray BuildColumnArray(ColumnSchema column, IReadOnlyList<ViewDefinitionRow> rows)
    {
        string fhirType = column.Type?.ToLowerInvariant() ?? "string";

        return fhirType switch
        {
            "boolean" => BuildBooleanArray(column.Name, rows),
            "integer" or "positiveint" or "unsignedint" => BuildInt32Array(column.Name, rows),
            "integer64" => BuildInt64Array(column.Name, rows),
            "decimal" => BuildDoubleArray(column.Name, rows),
            _ => BuildStringArray(column.Name, rows),
        };
    }

    private static BooleanArray BuildBooleanArray(string columnName, IReadOnlyList<ViewDefinitionRow> rows)
    {
        var builder = new BooleanArray.Builder();
        foreach (ViewDefinitionRow row in rows)
        {
            object? value = row[columnName];
            if (value is null)
            {
                builder.AppendNull();
            }
            else
            {
                builder.Append(Convert.ToBoolean(value));
            }
        }

        return builder.Build();
    }

    private static Int32Array BuildInt32Array(string columnName, IReadOnlyList<ViewDefinitionRow> rows)
    {
        var builder = new Int32Array.Builder();
        foreach (ViewDefinitionRow row in rows)
        {
            object? value = row[columnName];
            if (value is null)
            {
                builder.AppendNull();
            }
            else
            {
                builder.Append(Convert.ToInt32(value));
            }
        }

        return builder.Build();
    }

    private static Int64Array BuildInt64Array(string columnName, IReadOnlyList<ViewDefinitionRow> rows)
    {
        var builder = new Int64Array.Builder();
        foreach (ViewDefinitionRow row in rows)
        {
            object? value = row[columnName];
            if (value is null)
            {
                builder.AppendNull();
            }
            else
            {
                builder.Append(Convert.ToInt64(value));
            }
        }

        return builder.Build();
    }

    private static DoubleArray BuildDoubleArray(string columnName, IReadOnlyList<ViewDefinitionRow> rows)
    {
        var builder = new DoubleArray.Builder();
        foreach (ViewDefinitionRow row in rows)
        {
            object? value = row[columnName];
            if (value is null)
            {
                builder.AppendNull();
            }
            else
            {
                builder.Append(Convert.ToDouble(value));
            }
        }

        return builder.Build();
    }

    private static StringArray BuildStringArray(string columnName, IReadOnlyList<ViewDefinitionRow> rows)
    {
        var builder = new StringArray.Builder();
        foreach (ViewDefinitionRow row in rows)
        {
            object? value = row[columnName];
            if (value is null)
            {
                builder.AppendNull();
            }
            else
            {
                builder.Append(value.ToString() ?? string.Empty);
            }
        }

        return builder.Build();
    }

    private static IArrowType MapFhirTypeToArrowType(string? fhirType)
    {
        return fhirType?.ToLowerInvariant() switch
        {
            "boolean" => BooleanType.Default,
            "integer" or "positiveint" or "unsignedint" => Int32Type.Default,
            "integer64" => Int64Type.Default,
            "decimal" => DoubleType.Default,
            _ => StringType.Default,
        };
    }

    private string GetTableUri(string viewDefinitionName)
    {
        string baseUri = _config.StorageAccountUri?.TrimEnd('/') ?? throw new InvalidOperationException(
            "StorageAccountUri must be configured for Delta Lake materialization. " +
            "Set SqlOnFhirMaterialization:StorageAccountUri in appsettings.json " +
            "(e.g., abfss://workspace@onelake.dfs.fabric.microsoft.com/lakehouse/Tables).");

        // For abfss:// URIs (OneLake / ADLS Gen2), the full path is already in the URI.
        // For https:// URIs (Blob Storage), append the container.
        if (baseUri.StartsWith("abfss://", StringComparison.OrdinalIgnoreCase))
        {
            return $"{baseUri}/{viewDefinitionName}";
        }

        return $"{baseUri}/{_config.DefaultContainer}/{viewDefinitionName}";
    }

    private Dictionary<string, string> GetStorageOptions()
    {
        var options = new Dictionary<string, string>();

        if (!string.IsNullOrWhiteSpace(_config.StorageAccountConnection))
        {
            options["connection_string"] = _config.StorageAccountConnection;
        }
        else if (!string.IsNullOrWhiteSpace(_config.StorageAccountUri))
        {
            // Use DefaultAzureCredential for Managed Identity / az login authentication.
            // This is the standard auth path for Fabric / OneLake.
            try
            {
                var credential = new global::Azure.Identity.DefaultAzureCredential();
                var tokenContext = new global::Azure.Core.TokenRequestContext(AzureStorageScopes);
                var token = credential.GetToken(tokenContext, default);
                options["bearer_token"] = token.Token;
            }
            catch (Exception ex)
            {
                string message = "Failed to obtain Azure storage bearer token via DefaultAzureCredential. " +
                    "Ensure Managed Identity or az login credentials are available";
                _logger.LogWarning(ex, "{Message}", message);
            }
        }

        return options;
    }

    private IReadOnlyList<ColumnSchema> GetColumnSchema(string viewDefinitionJson)
    {
        var viewDefNode = JsonSourceNodeFactory.Parse(viewDefinitionJson).ToSourceNavigator();
        var expression = ViewDefinitionExpressionParser.Parse(viewDefNode);
        return _schemaEvaluator.GetSchema(expression);
    }
}
