// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using Ignixa.Serialization;
using Ignixa.SqlOnFhir.Evaluation;
using Ignixa.SqlOnFhir.Parsing;
using Ignixa.SqlOnFhir.Writers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.ExportDestinationClient;
using Microsoft.Health.Fhir.Core.Models;
using Parquet.Schema;

namespace Microsoft.Health.Fhir.SqlOnFhir.Materialization;

/// <summary>
/// Materializes ViewDefinition results as Parquet files to Azure Blob Storage or ADLS.
/// Each upsert appends rows to a timestamped Parquet file organized by ViewDefinition name.
/// Files are written to a temp path, then uploaded via <see cref="IExportDestinationClient"/>.
/// </summary>
public sealed class ParquetViewDefinitionMaterializer : IViewDefinitionMaterializer
{
    private readonly IViewDefinitionEvaluator _evaluator;
    private readonly IExportDestinationClient _exportDestinationClient;
    private readonly SqlOnFhirMaterializationConfiguration _config;
    private readonly SqlOnFhirSchemaEvaluator _schemaEvaluator;
    private readonly ILogger<ParquetViewDefinitionMaterializer> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ParquetViewDefinitionMaterializer"/> class.
    /// </summary>
    public ParquetViewDefinitionMaterializer(
        IViewDefinitionEvaluator evaluator,
        IExportDestinationClient exportDestinationClient,
        IOptions<SqlOnFhirMaterializationConfiguration> config,
        ILogger<ParquetViewDefinitionMaterializer> logger)
    {
        _evaluator = evaluator;
        _exportDestinationClient = exportDestinationClient;
        _config = config.Value;
        _schemaEvaluator = new SqlOnFhirSchemaEvaluator();
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

        if (result.Rows.Count == 0)
        {
            _logger.LogDebug(
                "Resource '{ResourceKey}' produced zero rows for Parquet ViewDef '{ViewDef}'",
                resourceKey,
                viewDefinitionName);
            return 0;
        }

        // Get schema for Parquet column types
        IReadOnlyList<ColumnSchema> columns = GetColumnSchema(viewDefinitionJson);

        // Build column type map for ParquetFileWriter
        var columnTypeMap = columns.ToDictionary(c => c.Name, c => c.Type ?? "string");

        // Build Parquet schema
        var parquetFields = new List<DataField> { new DataField("_resource_key", typeof(string)) };

        foreach (ColumnSchema col in columns)
        {
            parquetFields.Add(new DataField(col.Name, MapToParquetClrType(col.Type)));
        }

        var parquetSchema = new ParquetSchema(parquetFields.ToArray());

        // Write to a temp file, then upload
        string timestamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH.mm.ss.fffZ", CultureInfo.InvariantCulture);
        string blobName = $"{viewDefinitionName}/{DateTimeOffset.UtcNow:yyyy}/{DateTimeOffset.UtcNow:MM}/{DateTimeOffset.UtcNow:dd}/{timestamp}.parquet";

        string tempPath = Path.Combine(Path.GetTempPath(), $"sqlfhir_{Guid.NewGuid():N}.parquet");

        try
        {
            // Write rows to Parquet temp file
            await using (var writer = new ParquetFileWriter(tempPath, parquetSchema, _logger, columnTypeMap))
            {
                foreach (ViewDefinitionRow row in result.Rows)
                {
                    var rowDict = new Dictionary<string, object?>(row.Columns)
                    {
                        ["_resource_key"] = resourceKey,
                    };

                    await writer.WriteRowAsync(rowDict, cancellationToken);
                }

                await writer.FlushAsync(cancellationToken);
            }

            // Upload to Azure Blob/ADLS
            await _exportDestinationClient.ConnectAsync(cancellationToken, _config.DefaultContainer);
            string fileContent = Convert.ToBase64String(await File.ReadAllBytesAsync(tempPath, cancellationToken));
            _exportDestinationClient.WriteFilePart(blobName, fileContent);
            _exportDestinationClient.CommitFile(blobName);

            _logger.LogDebug(
                "Wrote {RowCount} row(s) to Parquet file '{BlobName}' for resource '{ResourceKey}'",
                result.Rows.Count,
                blobName,
                resourceKey);
        }
        finally
        {
            // Clean up temp file
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }

        return result.Rows.Count;
    }

    /// <inheritdoc />
    public Task<int> DeleteResourceAsync(
        string viewDefinitionName,
        string resourceKey,
        CancellationToken cancellationToken)
    {
        // Parquet is append-only — deletes are handled by downstream compaction.
        // Log the delete intent for potential future compaction jobs.
        _logger.LogDebug(
            "Parquet materializer: delete requested for '{ResourceKey}' in '{ViewDefName}' (append-only, no action)",
            resourceKey,
            viewDefinitionName);

        return Task.FromResult(0);
    }

    private IReadOnlyList<ColumnSchema> GetColumnSchema(string viewDefinitionJson)
    {
        var viewDefNode = JsonSourceNodeFactory.Parse(viewDefinitionJson).ToSourceNavigator();
        var expression = ViewDefinitionExpressionParser.Parse(viewDefNode);
        return _schemaEvaluator.GetSchema(expression);
    }

    private static Type MapToParquetClrType(string? fhirType)
    {
        return fhirType?.ToLowerInvariant() switch
        {
            "boolean" => typeof(bool),
            "integer" or "positiveint" or "unsignedint" => typeof(int),
            "integer64" => typeof(long),
            "decimal" => typeof(double),
            "date" or "datetime" or "instant" => typeof(DateTimeOffset),
            _ => typeof(string),
        };
    }
}
