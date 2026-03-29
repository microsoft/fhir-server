// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;
using System.Text.Json;
using MediatR;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Operations.ViewDefinitionRun;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlOnFhir.Channels;
using Microsoft.Health.Fhir.SqlOnFhir.Materialization;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;

namespace Microsoft.Health.Fhir.SqlOnFhir.Operations;

/// <summary>
/// Handles the $viewdefinition-run operation. Two modes:
/// <list type="bullet">
///   <item>Inline ViewDefinition: evaluates on-the-fly via Ignixa against server resources</item>
///   <item>Registered ViewDefinition: reads from the materialized sqlfhir table (fast, already computed)</item>
/// </list>
/// </summary>
public sealed class ViewDefinitionRunHandler : IRequestHandler<ViewDefinitionRunRequest, ViewDefinitionRunResponse>
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private readonly IViewDefinitionEvaluator _evaluator;
    private readonly IViewDefinitionSchemaManager _schemaManager;
    private readonly IViewDefinitionSubscriptionManager _subscriptionManager;
    private readonly Func<IScoped<ISearchService>> _searchServiceFactory;
    private readonly IResourceDeserializer _resourceDeserializer;
    private readonly ISqlRetryService _sqlRetryService;
    private readonly ILogger<ViewDefinitionRunHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ViewDefinitionRunHandler"/> class.
    /// </summary>
    public ViewDefinitionRunHandler(
        IViewDefinitionEvaluator evaluator,
        IViewDefinitionSchemaManager schemaManager,
        IViewDefinitionSubscriptionManager subscriptionManager,
        Func<IScoped<ISearchService>> searchServiceFactory,
        IResourceDeserializer resourceDeserializer,
        ISqlRetryService sqlRetryService,
        ILogger<ViewDefinitionRunHandler> logger)
    {
        _evaluator = evaluator;
        _schemaManager = schemaManager;
        _subscriptionManager = subscriptionManager;
        _searchServiceFactory = searchServiceFactory;
        _resourceDeserializer = resourceDeserializer;
        _sqlRetryService = sqlRetryService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ViewDefinitionRunResponse> Handle(ViewDefinitionRunRequest request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.ViewDefinitionName))
        {
            return await RunFromMaterializedTableAsync(request.ViewDefinitionName, request.Format, request.Limit, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(request.ViewDefinitionJson))
        {
            return await RunInlineAsync(request.ViewDefinitionJson, request.Format, request.Limit, cancellationToken);
        }

        throw new InvalidOperationException("Either viewDefinitionJson or viewDefinitionName is required.");
    }

    private async Task<ViewDefinitionRunResponse> RunFromMaterializedTableAsync(
        string viewDefinitionName,
        string format,
        int? limit,
        CancellationToken cancellationToken)
    {
        if (!await _schemaManager.TableExistsAsync(viewDefinitionName, cancellationToken))
        {
            throw new InvalidOperationException($"Materialized table for ViewDefinition '{viewDefinitionName}' does not exist.");
        }

        string qualifiedTable = SqlServerViewDefinitionSchemaManager.GetQualifiedTableName(viewDefinitionName);
        string limitClause = limit.HasValue ? $"TOP ({limit.Value})" : string.Empty;
        string sql = $"SELECT {limitClause} * FROM {qualifiedTable}";

        var rows = new List<Dictionary<string, object?>>();

        #pragma warning disable CA2100
        using var cmd = new SqlCommand(sql);
        #pragma warning restore CA2100

        await _sqlRetryService.ExecuteSql(
            cmd,
            async (sqlCmd, ct) =>
            {
                using var reader = await sqlCmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    var row = new Dictionary<string, object?>();
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        row[reader.GetName(i)] = await reader.IsDBNullAsync(i, ct) ? null : reader.GetValue(i);
                    }

                    rows.Add(row);
                }
            },
            _logger,
            $"ViewDefinitionRun:read:{viewDefinitionName}",
            cancellationToken,
            isReadOnly: true);

        _logger.LogInformation(
            "$viewdefinition-run read {RowCount} rows from materialized table '{ViewDefName}'",
            rows.Count,
            viewDefinitionName);

        return FormatResponse(rows, format);
    }

    private async Task<ViewDefinitionRunResponse> RunInlineAsync(
        string viewDefinitionJson,
        string format,
        int? limit,
        CancellationToken cancellationToken)
    {
        using var doc = JsonDocument.Parse(viewDefinitionJson);
        string resourceType = doc.RootElement.TryGetProperty("resource", out var resEl)
            ? resEl.GetString() ?? throw new InvalidOperationException("ViewDefinition must specify a 'resource' type.")
            : throw new InvalidOperationException("ViewDefinition must specify a 'resource' type.");

        using IScoped<ISearchService> searchScope = _searchServiceFactory();
        var queryParams = new List<Tuple<string, string>>
        {
            Tuple.Create("_count", (limit ?? 1000).ToString()),
        };

        SearchResult searchResult = await searchScope.Value.SearchAsync(
            resourceType,
            queryParams,
            cancellationToken,
            isAsyncOperation: true);

        var allRows = new List<Dictionary<string, object?>>();

        foreach (SearchResultEntry entry in searchResult.Results)
        {
            ResourceElement resource = _resourceDeserializer.Deserialize(entry.Resource);
            ViewDefinitionResult evalResult = _evaluator.Evaluate(viewDefinitionJson, resource);

            foreach (ViewDefinitionRow row in evalResult.Rows)
            {
                allRows.Add(new Dictionary<string, object?>(row.Columns));

                if (limit.HasValue && allRows.Count >= limit.Value)
                {
                    break;
                }
            }

            if (limit.HasValue && allRows.Count >= limit.Value)
            {
                break;
            }
        }

        _logger.LogInformation(
            "$viewdefinition-run evaluated inline ViewDefinition against {ResourceType}, produced {RowCount} rows",
            resourceType,
            allRows.Count);

        return FormatResponse(allRows, format);
    }

    internal static ViewDefinitionRunResponse FormatResponse(List<Dictionary<string, object?>> rows, string format)
    {
        return format.ToLowerInvariant() switch
        {
            "csv" => FormatAsCsv(rows),
            "ndjson" => FormatAsNdjson(rows),
            _ => FormatAsJson(rows),
        };
    }

    internal static ViewDefinitionRunResponse FormatAsJson(List<Dictionary<string, object?>> rows)
    {
        string json = JsonSerializer.Serialize(rows, JsonOptions);
        return new ViewDefinitionRunResponse(json, "application/json", rows.Count);
    }

    internal static ViewDefinitionRunResponse FormatAsNdjson(List<Dictionary<string, object?>> rows)
    {
        var sb = new StringBuilder();
        foreach (var row in rows)
        {
            sb.AppendLine(JsonSerializer.Serialize(row));
        }

        return new ViewDefinitionRunResponse(sb.ToString().TrimEnd(), "application/x-ndjson", rows.Count);
    }

    internal static ViewDefinitionRunResponse FormatAsCsv(List<Dictionary<string, object?>> rows)
    {
        if (rows.Count == 0)
        {
            return new ViewDefinitionRunResponse(string.Empty, "text/csv", 0);
        }

        var sb = new StringBuilder();
        var columns = rows[0].Keys.ToList();
        sb.AppendLine(string.Join(",", columns));

        foreach (var row in rows)
        {
            var values = columns.Select(col =>
            {
                object? val = row.TryGetValue(col, out var v) ? v : null;
                string strVal = val?.ToString() ?? string.Empty;

                if (strVal.Contains(',', StringComparison.Ordinal) || strVal.Contains('"', StringComparison.Ordinal) || strVal.Contains('\n', StringComparison.Ordinal))
                {
                    return $"\"{strVal.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
                }

                return strVal;
            });

            sb.AppendLine(string.Join(",", values));
        }

        return new ViewDefinitionRunResponse(sb.ToString().TrimEnd(), "text/csv", rows.Count);
    }
}
