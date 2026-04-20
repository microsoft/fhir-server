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

    // One-time diagnostic log of resolved column types per ViewDefinition — emits the
    // (columnName, path, evaluatorType, resolvedType, arrowType) tuple so we can verify
    // in App Insights that type inference is producing the intended Arrow types.
    private readonly ConcurrentDictionary<string, byte> _schemaDiagnosticLogged = new();

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
    public async Task<bool> EnsureStorageAsync(string viewDefinitionJson, string viewDefinitionName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(viewDefinitionJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(viewDefinitionName);

        string tableUri = GetTableUri(viewDefinitionName);
        Dictionary<string, string> storageOptions = GetStorageOptions();

        // Fast path: table already exists, nothing to do.
        try
        {
            using ITable existing = await _engine.LoadTableAsync(
                new TableOptions { TableLocation = tableUri, StorageOptions = storageOptions },
                cancellationToken);
            _logger.LogDebug("Delta Lake table for '{ViewDefName}' already exists at '{TableUri}'", viewDefinitionName, tableUri);
            return false;
        }
        catch
        {
            // Table does not exist — create it with the correct Arrow schema derived from the ViewDefinition
            // so it is immediately visible in Fabric/OneLake even if no FHIR resources match yet.
        }

        IReadOnlyList<ColumnSchema> columns = GetColumnSchema(viewDefinitionJson);
        IReadOnlyDictionary<string, (string? Path, string? ExplicitType)> columnMeta =
            ViewDefinitionTypeInferrer.ExtractColumnMetadata(viewDefinitionJson);

        LogSchemaDiagnosticIfNeeded(viewDefinitionName, columns, columnMeta);

        Apache.Arrow.Schema arrowSchema = BuildArrowSchema(columns, columnMeta);

        SemaphoreSlim tableLock = _tableLocks.GetOrAdd(viewDefinitionName, _ => new SemaphoreSlim(1, 1));
        await tableLock.WaitAsync(cancellationToken);
        try
        {
            // Re-check under the lock to avoid a create race with a concurrent upsert.
            try
            {
                using ITable existing = await _engine.LoadTableAsync(
                    new TableOptions { TableLocation = tableUri, StorageOptions = storageOptions },
                    cancellationToken);
                return false;
            }
            catch
            {
                // still missing — create below
            }

            using ITable table = await _engine.CreateTableAsync(
                new TableCreateOptions(tableUri, arrowSchema)
                {
                    StorageOptions = storageOptions,
                    SaveMode = SaveMode.ErrorIfExists,
                },
                cancellationToken);

            // Write a checkpoint so Fabric's lakehouse catalog can recognize and index the empty
            // table immediately, without waiting for the first data write.
            try
            {
                await table.CheckpointAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(
                    ex,
                    "Created empty Delta Lake table for '{ViewDefName}' but initial checkpoint failed; Fabric may show the table as 'Unidentified' until a checkpoint is created",
                    viewDefinitionName);
            }

            _logger.LogInformation(
                "Created empty Delta Lake table for '{ViewDefName}' at '{TableUri}' with {ColumnCount} column(s)",
                viewDefinitionName,
                tableUri,
                columns.Count + 1);

            return true;
        }
        finally
        {
            tableLock.Release();
        }
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
    public async Task CleanupStorageAsync(string viewDefinitionName, CancellationToken cancellationToken)
    {
        string tableUri = GetTableUri(viewDefinitionName);

        try
        {
            if (!TryParseDataLakeLocation(tableUri, out Uri? serviceUri, out string? fileSystem, out string? directoryPath))
            {
                _logger.LogWarning(
                    "Delta Lake cleanup for '{ViewDefName}' skipped — could not parse table URI '{TableUri}' as an ADLS Gen2 / OneLake location",
                    viewDefinitionName,
                    tableUri);
                return;
            }

            var credential = new global::Azure.Identity.DefaultAzureCredential();
            var serviceClient = new global::Azure.Storage.Files.DataLake.DataLakeServiceClient(serviceUri, credential);
            global::Azure.Storage.Files.DataLake.DataLakeFileSystemClient fsClient = serviceClient.GetFileSystemClient(fileSystem);
            global::Azure.Storage.Files.DataLake.DataLakeDirectoryClient dirClient = fsClient.GetDirectoryClient(directoryPath);

            global::Azure.Response<bool> exists = await dirClient.ExistsAsync(cancellationToken);
            if (!exists.Value)
            {
                _logger.LogInformation(
                    "Delta Lake cleanup for '{ViewDefName}': directory '{TableUri}' does not exist — nothing to delete",
                    viewDefinitionName,
                    tableUri);
                return;
            }

            await dirClient.DeleteAsync(cancellationToken: cancellationToken);

            // Invalidate any cached lock/semaphore — a future create should start fresh.
            _tableLocks.TryRemove(viewDefinitionName, out SemaphoreSlim? removedLock);
            removedLock?.Dispose();

            _logger.LogInformation(
                "Delta Lake cleanup for '{ViewDefName}' — deleted directory '{TableUri}'",
                viewDefinitionName,
                tableUri);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to delete Delta Lake directory for '{ViewDefName}' at '{TableUri}'. Storage may need manual cleanup.",
                viewDefinitionName,
                tableUri);
        }
    }

    /// <summary>
    /// Parses a Delta Lake table location (abfss://, https://, http://) into the
    /// components needed by <see cref="global::Azure.Storage.Files.DataLake.DataLakeServiceClient"/>.
    /// </summary>
    /// <remarks>
    /// Supported forms:
    /// <list type="bullet">
    /// <item><c>abfss://{filesystem}@{account}.dfs.{suffix}/{path}</c> — OneLake / ADLS Gen2.</item>
    /// <item><c>https://{account}.dfs.{suffix}/{filesystem}/{path}</c> — ADLS Gen2 via HTTPS.</item>
    /// </list>
    /// </remarks>
    internal static bool TryParseDataLakeLocation(
        string tableUri,
        out Uri? serviceUri,
        out string? fileSystem,
        out string? directoryPath)
    {
        serviceUri = null;
        fileSystem = null;
        directoryPath = null;

        if (string.IsNullOrWhiteSpace(tableUri))
        {
            return false;
        }

        if (tableUri.StartsWith("abfss://", StringComparison.OrdinalIgnoreCase))
        {
            // abfss://{filesystem}@{host}/{path}
            int schemeEnd = "abfss://".Length;
            int atIdx = tableUri.IndexOf('@', schemeEnd);
            if (atIdx <= schemeEnd)
            {
                return false;
            }

            fileSystem = tableUri.Substring(schemeEnd, atIdx - schemeEnd);

            int pathStart = tableUri.IndexOf('/', atIdx + 1);
            string host = pathStart < 0 ? tableUri.Substring(atIdx + 1) : tableUri.Substring(atIdx + 1, pathStart - (atIdx + 1));
            directoryPath = pathStart < 0 ? string.Empty : tableUri.Substring(pathStart + 1).TrimEnd('/');

            serviceUri = new Uri($"https://{host}");
            return !string.IsNullOrEmpty(fileSystem);
        }

        if (Uri.TryCreate(tableUri, UriKind.Absolute, out Uri? parsed) &&
            (parsed.Scheme == Uri.UriSchemeHttps || parsed.Scheme == Uri.UriSchemeHttp))
        {
            // https://{host}/{filesystem}/{path}
            string[] segments = parsed.AbsolutePath.Trim('/').Split('/', 2);
            if (segments.Length == 0 || string.IsNullOrEmpty(segments[0]))
            {
                return false;
            }

            fileSystem = segments[0];
            directoryPath = segments.Length > 1 ? segments[1].TrimEnd('/') : string.Empty;
            serviceUri = new Uri($"{parsed.Scheme}://{parsed.Host}");
            return true;
        }

        return false;
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
        IReadOnlyDictionary<string, (string? Path, string? ExplicitType)> columnMeta =
            ViewDefinitionTypeInferrer.ExtractColumnMetadata(viewDefinitionJson);
        LogSchemaDiagnosticIfNeeded(viewDefinitionName, columns, columnMeta);

        Apache.Arrow.Schema arrowSchema = BuildArrowSchema(columns, columnMeta);
        using RecordBatch recordBatch = BuildRecordBatch(arrowSchema, columns, result.Rows, resourceKey, columnMeta);

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
    public async Task<int> UpsertResourceBatchAsync(
        string viewDefinitionJson,
        string viewDefinitionName,
        IReadOnlyList<(ResourceElement Resource, string ResourceKey)> batch,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(viewDefinitionJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(viewDefinitionName);
        ArgumentNullException.ThrowIfNull(batch);

        if (batch.Count == 0)
        {
            return 0;
        }

        // Evaluate all resources locally into a single aggregated buffer keyed by resource_key.
        // This amortizes Delta Lake transaction overhead — one MERGE per batch instead of per resource.
        var aggregatedRows = new List<(string ResourceKey, ViewDefinitionRow Row)>(batch.Count * 2);
        var allResourceKeys = new HashSet<string>(batch.Count, StringComparer.Ordinal);

        foreach ((ResourceElement resource, string resourceKey) in batch)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            ArgumentNullException.ThrowIfNull(resource);
            ArgumentException.ThrowIfNullOrWhiteSpace(resourceKey);

            allResourceKeys.Add(resourceKey);

            ViewDefinitionResult evalResult = _evaluator.Evaluate(viewDefinitionJson, resource);
            foreach (ViewDefinitionRow row in evalResult.Rows)
            {
                aggregatedRows.Add((resourceKey, row));
            }
        }

        IReadOnlyList<ColumnSchema> columns = GetColumnSchema(viewDefinitionJson);
        IReadOnlyDictionary<string, (string? Path, string? ExplicitType)> columnMeta =
            ViewDefinitionTypeInferrer.ExtractColumnMetadata(viewDefinitionJson);

        LogSchemaDiagnosticIfNeeded(viewDefinitionName, columns, columnMeta);

        Apache.Arrow.Schema arrowSchema = BuildArrowSchema(columns, columnMeta);

        string tableUri = GetTableUri(viewDefinitionName);
        SemaphoreSlim tableLock = _tableLocks.GetOrAdd(viewDefinitionName, _ => new SemaphoreSlim(1, 1));

        await tableLock.WaitAsync(cancellationToken);
        try
        {
            using ITable table = await LoadOrCreateTableAsync(tableUri, arrowSchema, cancellationToken);

            // Step 1: delete all existing rows for resource keys in the batch. This handles both
            // the "replace" case (resource that previously produced N rows now produces M) and the
            // "zero-row resource" case (resource that no longer matches the ViewDefinition filter).
            // On an empty table (first-time population) this is a no-op commit.
            if (allResourceKeys.Count > 0)
            {
                string deletePredicate = BuildBatchDeletePredicate(allResourceKeys);
                await table.DeleteAsync(deletePredicate, cancellationToken);
            }

            // Step 2: insert all new rows in one MERGE. Because we just deleted all matching keys,
            // every source row hits the WHEN NOT MATCHED branch and becomes an insert — no spurious
            // per-row updates, and the MERGE stays simple.
            if (aggregatedRows.Count > 0)
            {
                using RecordBatch recordBatch = BuildBatchRecordBatch(arrowSchema, columns, columnMeta, aggregatedRows);
                string mergeSql = BuildMergeSql(viewDefinitionName, columns);
                await table.MergeAsync(mergeSql, [recordBatch], arrowSchema, cancellationToken);
            }

            _logger.LogDebug(
                "Delta Lake batch upsert: {ResourceCount} resources, {RowCount} rows in '{ViewDef}'",
                batch.Count,
                aggregatedRows.Count,
                viewDefinitionName);
        }
        finally
        {
            tableLock.Release();
        }

        return aggregatedRows.Count;
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
    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ReadRowsAsync(
        string viewDefinitionName,
        int? limit,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(viewDefinitionName);

        string tableUri = GetTableUri(viewDefinitionName);

        ITable table;
        try
        {
            table = await _engine.LoadTableAsync(
                new TableOptions { TableLocation = tableUri, StorageOptions = GetStorageOptions() },
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Delta Lake table '{ViewDefName}' not found at '{TableUri}'; returning empty result",
                viewDefinitionName,
                tableUri);
            return System.Array.Empty<IReadOnlyDictionary<string, object?>>();
        }

        using (table)
        {
            // The table alias provided to SelectQuery is what the engine binds the table to inside the query.
            const string tableAlias = "v";
            string limitClause = limit.HasValue ? $" LIMIT {limit.Value}" : string.Empty;
            var query = new SelectQuery($"SELECT * FROM {tableAlias}{limitClause}", tableAlias);

            var rows = new List<IReadOnlyDictionary<string, object?>>();

            await foreach (RecordBatch batch in table.QueryAsync(query, cancellationToken).WithCancellation(cancellationToken))
            {
                using (batch)
                {
                    AppendRecordBatchRows(batch, rows);

                    if (limit.HasValue && rows.Count >= limit.Value)
                    {
                        break;
                    }
                }
            }

            if (limit.HasValue && rows.Count > limit.Value)
            {
                rows = rows.Take(limit.Value).ToList();
            }

            _logger.LogDebug(
                "Delta Lake read {RowCount} rows from '{ViewDefName}' at '{TableUri}'",
                rows.Count,
                viewDefinitionName,
                tableUri);

            return rows;
        }
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
                new TableCreateOptions(tableUri, schema)
                {
                    StorageOptions = storageOptions,
                    SaveMode = SaveMode.ErrorIfExists,
                },
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
    /// Applies FHIRPath-aware type inference via <see cref="ViewDefinitionTypeInferrer"/>.
    /// </summary>
    internal static Apache.Arrow.Schema BuildArrowSchema(
        IReadOnlyList<ColumnSchema> columns,
        IReadOnlyDictionary<string, (string? Path, string? ExplicitType)>? columnMeta = null)
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
            string? resolvedType = ResolveColumnType(col, columnMeta);
            IArrowType arrowType = MapFhirTypeToArrowType(resolvedType);
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
    /// Builds an Apache Arrow RecordBatch for a single resource's rows.
    /// </summary>
    internal static RecordBatch BuildRecordBatch(
        Apache.Arrow.Schema schema,
        IReadOnlyList<ColumnSchema> columns,
        IReadOnlyList<ViewDefinitionRow> rows,
        string resourceKey,
        IReadOnlyDictionary<string, (string? Path, string? ExplicitType)>? columnMeta = null)
    {
        int rowCount = rows.Count;
        var arrays = new List<IArrowArray>();

        var resourceKeyBuilder = new StringArray.Builder();
        for (int i = 0; i < rowCount; i++)
        {
            resourceKeyBuilder.Append(resourceKey);
        }

        arrays.Add(resourceKeyBuilder.Build());

        foreach (ColumnSchema col in columns)
        {
            string? resolvedType = ResolveColumnType(col, columnMeta);
            IArrowArray array = BuildColumnArray(col.Name, resolvedType, rows);
            arrays.Add(array);
        }

        return new RecordBatch(schema, arrays, rowCount);
    }

    /// <summary>
    /// Builds an Apache Arrow RecordBatch that aggregates rows from many resources. Each (resourceKey, row)
    /// pair contributes exactly one Arrow row; <c>_resource_key</c> is populated from the pair's first element.
    /// </summary>
    internal static RecordBatch BuildBatchRecordBatch(
        Apache.Arrow.Schema schema,
        IReadOnlyList<ColumnSchema> columns,
        IReadOnlyDictionary<string, (string? Path, string? ExplicitType)>? columnMeta,
        IReadOnlyList<(string ResourceKey, ViewDefinitionRow Row)> rows)
    {
        int rowCount = rows.Count;
        var arrays = new List<IArrowArray>();

        var resourceKeyBuilder = new StringArray.Builder();
        foreach ((string rk, ViewDefinitionRow _) in rows)
        {
            resourceKeyBuilder.Append(rk);
        }

        arrays.Add(resourceKeyBuilder.Build());

        // Project to a view of just the rows for BuildColumnArray.
        var rowView = new ViewDefinitionRowProjection(rows);

        foreach (ColumnSchema col in columns)
        {
            string? resolvedType = ResolveColumnType(col, columnMeta);
            IArrowArray array = BuildColumnArray(col.Name, resolvedType, rowView);
            arrays.Add(array);
        }

        return new RecordBatch(schema, arrays, rowCount);
    }

    /// <summary>
    /// Builds a SQL predicate that matches any row whose <c>_resource_key</c> is in the provided set.
    /// Uses quoted <c>IN</c> list with SQL-escaped single quotes.
    /// </summary>
    internal static string BuildBatchDeletePredicate(IEnumerable<string> resourceKeys)
    {
        IEnumerable<string> quoted = resourceKeys
            .Select(k => "'" + k.Replace("'", "''", StringComparison.Ordinal) + "'");
        return $"{IViewDefinitionSchemaManager.ResourceKeyColumnName} IN ({string.Join(",", quoted)})";
    }

    /// <summary>
    /// Resolves the most precise FHIR type for a column by combining Ignixa's evaluator-inferred
    /// type with the explicit-type/FHIRPath-based inference from <see cref="ViewDefinitionTypeInferrer"/>.
    /// </summary>
    private static string? ResolveColumnType(
        ColumnSchema col,
        IReadOnlyDictionary<string, (string? Path, string? ExplicitType)>? columnMeta)
    {
        string? path = null;
        string? explicitType = null;
        if (columnMeta != null && columnMeta.TryGetValue(col.Name, out (string? Path, string? ExplicitType) meta))
        {
            path = meta.Path;
            explicitType = meta.ExplicitType;
        }

        return ViewDefinitionTypeInferrer.ResolveType(path, explicitType, col.Type);
    }

    private void LogSchemaDiagnosticIfNeeded(
        string viewDefinitionName,
        IReadOnlyList<ColumnSchema> columns,
        IReadOnlyDictionary<string, (string? Path, string? ExplicitType)> columnMeta)
    {
        if (!_schemaDiagnosticLogged.TryAdd(viewDefinitionName, 0))
        {
            return;
        }

        foreach (ColumnSchema col in columns)
        {
            columnMeta.TryGetValue(col.Name, out (string? Path, string? ExplicitType) meta);
            string? resolved = ViewDefinitionTypeInferrer.ResolveType(meta.Path, meta.ExplicitType, col.Type);
            string arrowTypeName = MapFhirTypeToArrowType(resolved).Name;

            _logger.LogWarning(
                "[VDPopulate] Column type for '{ViewDef}'.{ColumnName}: path='{Path}', explicit='{Explicit}', ignixa='{Ignixa}', resolved='{Resolved}', arrow={Arrow}",
                viewDefinitionName,
                col.Name,
                meta.Path ?? "(null)",
                meta.ExplicitType ?? "(null)",
                col.Type ?? "(null)",
                resolved ?? "(null)",
                arrowTypeName);
        }
    }

    private static IArrowArray BuildColumnArray(string columnName, string? resolvedFhirType, IReadOnlyList<ViewDefinitionRow> rows)
    {
        string fhirType = resolvedFhirType?.ToLowerInvariant() ?? "string";

        return fhirType switch
        {
            "boolean" => BuildBooleanArray(columnName, rows),
            "integer" or "positiveint" or "unsignedint" => BuildInt32Array(columnName, rows),
            "integer64" => BuildInt64Array(columnName, rows),
            "decimal" => BuildDoubleArray(columnName, rows),
            "datetime" or "instant" => BuildTimestampArray(columnName, rows),
            "date" => BuildDate32Array(columnName, rows),
            "time" => BuildTime32Array(columnName, rows),
            _ => BuildStringArray(columnName, rows),
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
                builder.Append(Convert.ToBoolean(value, System.Globalization.CultureInfo.InvariantCulture));
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
                builder.Append(Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture));
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
                builder.Append(Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture));
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
                builder.Append(Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        return builder.Build();
    }

    private static TimestampArray BuildTimestampArray(string columnName, IReadOnlyList<ViewDefinitionRow> rows)
    {
        var builder = new TimestampArray.Builder(new TimestampType(TimeUnit.Microsecond, "UTC"));
        foreach (ViewDefinitionRow row in rows)
        {
            object? value = row[columnName];
            if (value is null)
            {
                builder.AppendNull();
                continue;
            }

            if (TryConvertToDateTimeOffset(value, out DateTimeOffset dto))
            {
                builder.Append(dto.ToUniversalTime());
            }
            else
            {
                builder.AppendNull();
            }
        }

        return builder.Build();
    }

    private static Date32Array BuildDate32Array(string columnName, IReadOnlyList<ViewDefinitionRow> rows)
    {
        var builder = new Date32Array.Builder();
        foreach (ViewDefinitionRow row in rows)
        {
            object? value = row[columnName];
            if (value is null)
            {
                builder.AppendNull();
                continue;
            }

            if (TryConvertToDateTimeOffset(value, out DateTimeOffset dto))
            {
                builder.Append(dto.UtcDateTime.Date);
            }
            else
            {
                builder.AppendNull();
            }
        }

        return builder.Build();
    }

    private static Time32Array BuildTime32Array(string columnName, IReadOnlyList<ViewDefinitionRow> rows)
    {
        var builder = new Time32Array.Builder();
        foreach (ViewDefinitionRow row in rows)
        {
            object? value = row[columnName];
            if (value is null)
            {
                builder.AppendNull();
                continue;
            }

            if (TryConvertToTimeSpan(value, out TimeSpan ts))
            {
                // Time32 default unit is milliseconds since midnight.
                builder.Append((int)ts.TotalMilliseconds);
            }
            else
            {
                builder.AppendNull();
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

    private static bool TryConvertToDateTimeOffset(object value, out DateTimeOffset result)
    {
        switch (value)
        {
            case DateTimeOffset dto:
                result = dto;
                return true;
            case DateTime dt:
                result = dt.Kind == DateTimeKind.Unspecified
                    ? new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc), TimeSpan.Zero)
                    : new DateTimeOffset(dt.ToUniversalTime(), TimeSpan.Zero);
                return true;
            case string s when !string.IsNullOrWhiteSpace(s):
                return DateTimeOffset.TryParse(
                    s,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                    out result);
            default:
                // Fallback for Firely System primitives (Hl7.Fhir.ElementModel.Types.DateTime/Date),
                // partial-precision dates, and any wrapper type that round-trips via ISO 8601 ToString().
                // Avoids taking a hard dependency on the Firely type names.
                string? text = value.ToString();
                if (!string.IsNullOrWhiteSpace(text)
                    && DateTimeOffset.TryParse(
                        text,
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                        out result))
                {
                    return true;
                }

                result = default;
                return false;
        }
    }

    private static bool TryConvertToTimeSpan(object value, out TimeSpan result)
    {
        switch (value)
        {
            case TimeSpan ts:
                result = ts;
                return true;
            case string s when !string.IsNullOrWhiteSpace(s):
                return TimeSpan.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, out result);
            default:
                // Fallback for Firely System.Time and other ISO-string-friendly wrappers.
                string? text = value.ToString();
                if (!string.IsNullOrWhiteSpace(text)
                    && TimeSpan.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, out result))
                {
                    return true;
                }

                result = default;
                return false;
        }
    }

    private static IArrowType MapFhirTypeToArrowType(string? fhirType)
    {
        return fhirType?.ToLowerInvariant() switch
        {
            "boolean" => BooleanType.Default,
            "integer" or "positiveint" or "unsignedint" => Int32Type.Default,
            "integer64" => Int64Type.Default,
            "decimal" => DoubleType.Default,
            "datetime" or "instant" => new TimestampType(TimeUnit.Microsecond, "UTC"),
            "date" => Date32Type.Default,
            "time" => Time32Type.Default,
            _ => StringType.Default,
        };
    }

    private string GetTableUri(string viewDefinitionName)
    {
        string baseUri = _config.StorageAccountUri?.TrimEnd('/') ?? throw new InvalidOperationException(
            "StorageAccountUri must be configured for Delta Lake materialization. " +
            "Set SqlOnFhirMaterialization:StorageAccountUri in appsettings.json " +
            "(e.g., abfss://workspace@onelake.dfs.fabric.microsoft.com/lakehouse/Tables).");

        // Schema-enabled Fabric lakehouses expect Tables/{schema}/{tableName}/.
        // When DeltaSchema is configured (default "dbo"), insert it before the table name.
        string? schema = string.IsNullOrWhiteSpace(_config.DeltaSchema) ? null : _config.DeltaSchema.Trim('/');
        string tableSegment = schema is null ? viewDefinitionName : $"{schema}/{viewDefinitionName}";

        // For abfss:// URIs (OneLake / ADLS Gen2), the full path is already in the URI.
        // For https:// URIs (Blob Storage), append the container.
        if (baseUri.StartsWith("abfss://", StringComparison.OrdinalIgnoreCase))
        {
            return $"{baseUri}/{tableSegment}";
        }

        return $"{baseUri}/{_config.DefaultContainer}/{tableSegment}";
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

    /// <summary>
    /// Materializes an Arrow <see cref="RecordBatch"/> as a list of column-name → object dictionaries,
    /// one entry per row, decoded according to each column's Arrow type. Used by <see cref="ReadRowsAsync"/>.
    /// </summary>
    internal static void AppendRecordBatchRows(RecordBatch batch, List<IReadOnlyDictionary<string, object?>> rows)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(rows);

        int rowCount = batch.Length;
        int colCount = batch.ColumnCount;
        if (rowCount == 0 || colCount == 0)
        {
            return;
        }

        string[] columnNames = new string[colCount];
        IArrowArray[] arrays = new IArrowArray[colCount];
        for (int c = 0; c < colCount; c++)
        {
            columnNames[c] = batch.Schema.GetFieldByIndex(c).Name;
            arrays[c] = batch.Column(c);
        }

        for (int r = 0; r < rowCount; r++)
        {
            var row = new Dictionary<string, object?>(colCount, StringComparer.Ordinal);
            for (int c = 0; c < colCount; c++)
            {
                row[columnNames[c]] = ReadArrowValue(arrays[c], r);
            }

            rows.Add(row);
        }
    }

    /// <summary>
    /// Reads a single value at row <paramref name="rowIndex"/> from an Arrow array, returning
    /// a CLR object that round-trips cleanly through System.Text.Json. Unknown array types
    /// fall back to <c>ToString()</c>.
    /// </summary>
    internal static object? ReadArrowValue(IArrowArray array, int rowIndex)
    {
        if (array.IsNull(rowIndex))
        {
            return null;
        }

        switch (array)
        {
            case StringArray s:
                return s.GetString(rowIndex);
            case StringViewArray sv:
                return sv.GetString(rowIndex);
            case LargeStringArray ls:
                return ls.GetString(rowIndex);
            case BooleanArray b:
                return b.GetValue(rowIndex);
            case Int32Array i32:
                return i32.GetValue(rowIndex);
            case Int64Array i64:
                return i64.GetValue(rowIndex);
            case DoubleArray d:
                return d.GetValue(rowIndex);
            case FloatArray f:
                return f.GetValue(rowIndex);
            case TimestampArray ts:
                return ts.GetTimestamp(rowIndex);
            case Date32Array d32:
                {
                    DateTimeOffset? v = d32.GetDateTimeOffset(rowIndex);
                    return v?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
                }

            case Date64Array d64:
                {
                    DateTimeOffset? v = d64.GetDateTimeOffset(rowIndex);
                    return v?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
                }

            case Time32Array t32:
                {
                    int? millis = t32.GetValue(rowIndex);
                    return millis.HasValue ? TimeSpan.FromMilliseconds(millis.Value).ToString(@"hh\:mm\:ss\.fff", System.Globalization.CultureInfo.InvariantCulture) : null;
                }

            case Time64Array t64:
                {
                    long? micros = t64.GetValue(rowIndex);
                    return micros.HasValue ? TimeSpan.FromTicks(micros.Value * 10).ToString(@"hh\:mm\:ss\.ffffff", System.Globalization.CultureInfo.InvariantCulture) : null;
                }

            default:
                return array.ToString();
        }
    }

    /// <summary>
    /// Projects an aggregated list of (resourceKey, row) pairs as an IReadOnlyList&lt;ViewDefinitionRow&gt;
    /// so the per-column Arrow array builders can iterate without allocating a separate rows list.
    /// </summary>
    private sealed class ViewDefinitionRowProjection : IReadOnlyList<ViewDefinitionRow>
    {
        private readonly IReadOnlyList<(string ResourceKey, ViewDefinitionRow Row)> _source;

        public ViewDefinitionRowProjection(IReadOnlyList<(string ResourceKey, ViewDefinitionRow Row)> source)
        {
            _source = source;
        }

        public int Count => _source.Count;

        public ViewDefinitionRow this[int index] => _source[index].Row;

        public IEnumerator<ViewDefinitionRow> GetEnumerator()
        {
            foreach ((string _, ViewDefinitionRow row) in _source)
            {
                yield return row;
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
