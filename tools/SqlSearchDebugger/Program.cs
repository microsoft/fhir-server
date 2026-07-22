// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;
using Microsoft.Health.Fhir.Core;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Search;
using Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Extensions.DependencyInjection;
using MediatR;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// Initialize the FHIR model and parser
var modelInfoProvider = new VersionSpecificModelInfoProvider();
ModelInfoProvider.SetProvider(modelInfoProvider);

var fhirModel = new FakeSqlServerFhirModel();
var searchParamDefManager = InitializeSearchParameterDefinitionManager(modelInfoProvider);
var sqlSearchParamDefManager = new SqlSearchParameterDefinitionManager(searchParamDefManager, fhirModel);
var logger = LoggerFactory.Create(b => b.AddConsole()).CreateLogger<SearchParameterSqlParser>();
var parser = new SearchParameterSqlParser(sqlSearchParamDefManager, fhirModel, logger);

Console.WriteLine("SQL Search Debugger initialized with {0} resource types and {1} search parameters",
    fhirModel.ResourceTypeCount, fhirModel.SearchParamCount);
Console.WriteLine("Open http://localhost:5200 in your browser");

// Serve the HTML GUI
app.MapGet("/", () => Results.Content(HtmlContent.GetIndexHtml(), "text/html"));

// API endpoint to parse a FHIR URL into SQL
app.MapPost("/api/parse", (ParseRequest request) =>
{
    try
    {
        var result = ParseFhirUrl(request.Url, request.ContinuationToken, parser, fhirModel);
        return Results.Json(result);
    }
    catch (Exception ex)
    {
        return Results.Json(new { error = ex.Message, stackTrace = ex.StackTrace }, statusCode: 200);
    }
});

// API endpoint to list known resource types
app.MapGet("/api/resource-types", () => Results.Json(fhirModel.GetAllResourceTypes()));

// API endpoint to list search params for a resource type
app.MapGet("/api/search-params/{resourceType}", (string resourceType) =>
{
    try
    {
        var typeId = fhirModel.GetResourceTypeId(resourceType);
        var parameters = sqlSearchParamDefManager.GetByResourceType(typeId);
        return Results.Json(parameters.Select(p => new
        {
            code = p.SearchParameterInfo.Code,
            type = p.SearchParameterInfo.Type.ToString(),
            url = p.SearchParameterInfo.Url?.ToString(),
            id = p.Id,
            description = p.SearchParameterInfo.Description,
            targets = p.SearchParameterInfo.TargetResourceTypes?.Select(t => t.ToString()),
        }));
    }
    catch (Exception ex)
    {
        return Results.Json(new { error = ex.Message }, statusCode: 400);
    }
});

app.Run("http://localhost:5200");

// ----------------------------------- Helper Methods -----------------------------------

static object ParseFhirUrl(string url, string? continuationToken, SearchParameterSqlParser parser, FakeSqlServerFhirModel fhirModel)
{
    if (string.IsNullOrWhiteSpace(url))
    {
        throw new ArgumentException("URL cannot be empty");
    }

    // Strip leading slash
    url = url.TrimStart('/');

    string resourceType;
    string queryString = string.Empty;

    var questionIdx = url.IndexOf('?');
    if (questionIdx >= 0)
    {
        resourceType = url[..questionIdx];
        queryString = url[(questionIdx + 1)..];
    }
    else
    {
        resourceType = url;
    }

    // Handle paths like "Patient/$includes"
    var slashIdx = resourceType.IndexOf('/');
    if (slashIdx >= 0)
    {
        resourceType = resourceType[..slashIdx];
    }

    short resourceTypeId = fhirModel.GetResourceTypeId(resourceType);

    // Parse query string into parameters
    var parameters = new Dictionary<string, IList<string>>();
    parameters["_type"] = new List<string> { resourceType };

    if (!string.IsNullOrEmpty(queryString))
    {
        foreach (var param in queryString.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eqIdx = param.IndexOf('=');
            string key, value;
            if (eqIdx >= 0)
            {
                key = Uri.UnescapeDataString(param[..eqIdx]);
                value = Uri.UnescapeDataString(param[(eqIdx + 1)..]);
            }
            else
            {
                key = Uri.UnescapeDataString(param);
                value = string.Empty;
            }

            if (parameters.TryGetValue(key, out var existing))
            {
                existing.Add(value);
            }
            else
            {
                parameters[key] = new List<string> { value };
            }
        }
    }

    // Extract _count for MaxItemCount
    int maxItemCount = 10;
    if (parameters.TryGetValue("_count", out var countValues) && countValues.Count > 0)
    {
        if (int.TryParse(countValues[0], out var count) && count > 0)
        {
            maxItemCount = count;
        }
    }

    // Extract _sort for SearchOptions.Sort
    var sortList = new List<(SearchParameterInfo searchParameterInfo, SortOrder sortOrder)>();
    if (parameters.TryGetValue("_sort", out var sortValues) && sortValues.Count > 0)
    {
        var sortValue = sortValues[0];
        var sortDescending = sortValue.StartsWith('-');
        var sortParamName = sortDescending ? sortValue[1..] : sortValue;
        var sortOrder = sortDescending ? SortOrder.Descending : SortOrder.Ascending;
        sortList.Add((new SearchParameterInfo(sortParamName, sortParamName), sortOrder));
    }

    // Build SqlSearchOptions using internal constructors (via InternalsVisibleTo)
    var searchOptions = new SearchOptions();
    searchOptions.MaxItemCount = maxItemCount;
    searchOptions.IncludeCount = 1000;
    searchOptions.Sort = sortList;
    searchOptions.QueryParams = parameters;
    searchOptions.SearchParameters = new List<SearchParameterInfo>();
    searchOptions.UnsupportedSearchParams = new List<Tuple<string, string>>();

    var sqlSearchOptions = new SqlSearchOptions(searchOptions);

    // Parse continuation token if provided
    ContinuationToken? ct = null;
    if (!string.IsNullOrWhiteSpace(continuationToken))
    {
        ct = ContinuationToken.FromString(continuationToken);
    }

    // Generate SQL
    var sql = parser.ParseMultiple(parameters, sqlSearchOptions, ct);

    return new
    {
        resourceType,
        resourceTypeId,
        queryParameters = parameters,
        continuationTokenParsed = ct?.ToString(),
        generatedSql = sql,
        formattedSql = FormatSql(sql),
    };
}

static string? FormatSql(string? sql)
{
    if (string.IsNullOrWhiteSpace(sql))
    {
        return null;
    }

    return sql
        .Replace(";WITH ", ";\n\nWITH ")
        .Replace(", cte", ",\ncte")
        .Replace(" AS (", " AS (\n  ")
        .Replace("SELECT ", "\n  SELECT ")
        .Replace(" FROM ", "\n  FROM ")
        .Replace(" INNER JOIN ", "\n  INNER JOIN ")
        .Replace(" LEFT JOIN ", "\n  LEFT JOIN ")
        .Replace(" WHERE ", "\n  WHERE ")
        .Replace(" AND ", "\n    AND ")
        .Replace(" ORDER BY ", "\n  ORDER BY ")
        .Replace(" UNION ALL ", "\n  UNION ALL\n  ")
        .Replace(" GROUP BY ", "\n  GROUP BY ")
        .Replace(")\n", ")\n\n")
        .Replace(" OPTION ", "\n  OPTION ")
        .Trim();
}

static SearchParameterDefinitionManager InitializeSearchParameterDefinitionManager(IModelInfoProvider modelInfoProvider)
{
    var mediator = new FakeMediator();
    var scopeSearchService = new FakeScopeProvider<ISearchService>(null!);
    var scopeStatusStore = new FakeScopeProvider<ISearchParameterStatusDataStore>(null!);
    var scopeDataStore = new FakeScopeProvider<IFhirDataStore>(null!);
    var comparer = new FakeSearchParameterComparer();
    var logger = LoggerFactory.Create(b => b.AddConsole()).CreateLogger<SearchParameterDefinitionManager>();

    var manager = new SearchParameterDefinitionManager(
        modelInfoProvider,
        mediator,
        scopeSearchService,
        comparer,
        scopeStatusStore,
        scopeDataStore,
        logger);

    return manager;
}

// ----------------------------------- Models -----------------------------------

record ParseRequest(string Url, string? ContinuationToken);

// ----------------------------------- Fake Implementations -----------------------------------

class FakeSqlServerFhirModel : ISqlServerFhirModel
{
    private readonly Dictionary<string, short> _resourceTypeNameToId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<short, string> _resourceTypeIdToName = new();
    private readonly Dictionary<string, short> _searchParamUriToId = new();
    private readonly Dictionary<string, int> _systemToId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _quantityCodeToId = new(StringComparer.OrdinalIgnoreCase);
    private short _nextResourceTypeId = 1;
    private short _nextSearchParamId = 1;
    private int _nextSystemId = 1;
    private int _nextQuantityCodeId = 1;

    public int ResourceTypeCount => _resourceTypeNameToId.Count;
    public int SearchParamCount => _searchParamUriToId.Count;

    public (short lowestId, short highestId) ResourceTypeIdRange =>
        _resourceTypeNameToId.Count > 0
            ? ((short)1, (short)(_nextResourceTypeId - 1))
            : ((short)0, (short)0);

    public short GetResourceTypeId(string resourceTypeName)
    {
        if (_resourceTypeNameToId.TryGetValue(resourceTypeName, out var id))
        {
            return id;
        }

        id = _nextResourceTypeId++;
        _resourceTypeNameToId[resourceTypeName] = id;
        _resourceTypeIdToName[id] = resourceTypeName;
        return id;
    }

    public bool TryGetResourceTypeId(string resourceTypeName, out short id)
    {
        if (_resourceTypeNameToId.TryGetValue(resourceTypeName, out id))
        {
            return true;
        }

        id = GetResourceTypeId(resourceTypeName);
        return true;
    }

    public string GetResourceTypeName(short resourceTypeId)
    {
        if (_resourceTypeIdToName.TryGetValue(resourceTypeId, out var name))
        {
            return name;
        }

        return $"UnknownType_{resourceTypeId}";
    }

    public byte GetClaimTypeId(string claimTypeName) => 1;

    public short GetSearchParamId(Uri searchParamUri)
    {
        if (searchParamUri == null)
        {
            return 0;
        }

        var key = searchParamUri.OriginalString;
        if (_searchParamUriToId.TryGetValue(key, out var id))
        {
            return id;
        }

        id = _nextSearchParamId++;
        _searchParamUriToId[key] = id;
        return id;
    }

    public void TryAddSearchParamIdToUriMapping(string searchParamUri, short searchParamId)
    {
        _searchParamUriToId[searchParamUri] = searchParamId;
    }

    public void RemoveSearchParamIdToUriMapping(string searchParamUri)
    {
        _searchParamUriToId.Remove(searchParamUri);
    }

    public byte GetCompartmentTypeId(string compartmentType) => 1;

    public bool TryGetSystemId(string system, out int systemId)
    {
        if (_systemToId.TryGetValue(system, out systemId))
        {
            return true;
        }

        systemId = _nextSystemId++;
        _systemToId[system] = systemId;
        return true;
    }

    public int GetSystemId(string system)
    {
        TryGetSystemId(system, out var id);
        return id;
    }

    public int GetQuantityCodeId(string code)
    {
        TryGetQuantityCodeId(code, out var id);
        return id;
    }

    public bool TryGetQuantityCodeId(string code, out int quantityCodeId)
    {
        if (_quantityCodeToId.TryGetValue(code, out quantityCodeId))
        {
            return true;
        }

        quantityCodeId = _nextQuantityCodeId++;
        _quantityCodeToId[code] = quantityCodeId;
        return true;
    }

    public List<object> GetAllResourceTypes()
    {
        return _resourceTypeNameToId.Select(kvp => (object)new { name = kvp.Key, id = kvp.Value })
            .OrderBy(x => ((dynamic)x).id)
            .ToList();
    }
}

class FakeMediator : IMediator
{
    public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        => Task.FromResult(default(TResponse)!);

    public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest
        => Task.CompletedTask;

    public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        => Task.FromResult<object?>(null);

    public Task Publish(object notification, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default) where TNotification : INotification
        => Task.CompletedTask;

    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
        => EmptyAsyncEnumerable<TResponse>();

    public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
        => EmptyAsyncEnumerable<object?>();

    private static async IAsyncEnumerable<T> EmptyAsyncEnumerable<T>()
    {
        await Task.CompletedTask;
        yield break;
    }
}

class FakeScopeProvider<T> : IScopeProvider<T> where T : class
{
    private readonly T _instance;
    public FakeScopeProvider(T instance) => _instance = instance;
    public IScoped<T> Invoke() => new FakeScoped<T>(_instance);
}

class FakeScoped<T> : IScoped<T> where T : class
{
    public FakeScoped(T value) => Value = value;
    public T Value { get; }
    public void Dispose() { }
}

class FakeSearchParameterComparer : ISearchParameterComparer<SearchParameterInfo>
{
    public int Compare(SearchParameterInfo? x, SearchParameterInfo? y)
    {
        if (x == null && y == null) return 0;
        if (x == null) return -1;
        if (y == null) return 1;
        return string.Compare(x.Url?.ToString(), y.Url?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    public int CompareBase(IEnumerable<string> x, IEnumerable<string> y) => 0;
    public int CompareComponent(IEnumerable<(string definition, string expression)> x, IEnumerable<(string definition, string expression)> y) => 0;
    public int CompareExpression(string x, string y, bool isQuantity) => string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
}

// ----------------------------------- HTML Content -----------------------------------

static class HtmlContent
{
    public static string GetIndexHtml() => """
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>SQL Search Parser Debugger</title>
    <style>
        * { box-sizing: border-box; margin: 0; padding: 0; }
        body { font-family: 'Segoe UI', system-ui, sans-serif; background: #1e1e2e; color: #cdd6f4; min-height: 100vh; }
        .container { max-width: 1400px; margin: 0 auto; padding: 20px; }
        h1 { color: #89b4fa; margin-bottom: 20px; font-size: 1.8em; }
        h2 { color: #a6e3a1; margin: 15px 0 10px; font-size: 1.2em; }
        .input-section { background: #313244; border-radius: 8px; padding: 20px; margin-bottom: 20px; }
        .input-group { display: flex; gap: 10px; align-items: center; margin-bottom: 10px; }
        .input-group label { min-width: 140px; color: #bac2de; }
        input[type="text"] { flex: 1; padding: 10px 14px; border: 1px solid #45475a; border-radius: 6px; background: #1e1e2e; color: #cdd6f4; font-size: 14px; font-family: 'Cascadia Code', 'Fira Code', monospace; }
        input[type="text"]:focus { outline: none; border-color: #89b4fa; }
        button { padding: 10px 24px; border: none; border-radius: 6px; background: #89b4fa; color: #1e1e2e; font-weight: 600; cursor: pointer; font-size: 14px; }
        button:hover { background: #b4d0fb; }
        button:active { transform: scale(0.98); }
        .output-section { display: grid; grid-template-columns: 1fr 1fr; gap: 20px; }
        .panel { background: #313244; border-radius: 8px; padding: 20px; overflow: auto; }
        .panel.full-width { grid-column: 1 / -1; }
        pre { background: #1e1e2e; padding: 15px; border-radius: 6px; overflow-x: auto; font-size: 13px; line-height: 1.5; font-family: 'Cascadia Code', 'Fira Code', monospace; white-space: pre-wrap; word-break: break-word; }
        .sql-keyword { color: #cba6f7; font-weight: bold; }
        .sql-function { color: #f9e2af; }
        .sql-string { color: #a6e3a1; }
        .sql-number { color: #fab387; }
        .sql-table { color: #89dceb; }
        .sql-column { color: #f5c2e7; }
        .error { background: #45273a; border: 1px solid #f38ba8; color: #f38ba8; padding: 15px; border-radius: 6px; white-space: pre-wrap; font-family: monospace; font-size: 13px; }
        .params-table { width: 100%; border-collapse: collapse; }
        .params-table th, .params-table td { padding: 8px 12px; text-align: left; border-bottom: 1px solid #45475a; }
        .params-table th { color: #89b4fa; background: #1e1e2e; }
        .params-table td { font-family: 'Cascadia Code', monospace; font-size: 13px; }
        .badge { display: inline-block; padding: 2px 8px; border-radius: 4px; font-size: 12px; background: #45475a; color: #cdd6f4; margin: 2px; }
        .badge.type { background: #1e3a5f; color: #89b4fa; }
        .badge.id { background: #2d3a1e; color: #a6e3a1; }
        .examples { margin-top: 10px; }
        .examples a { color: #89b4fa; text-decoration: none; margin-right: 15px; font-size: 13px; cursor: pointer; }
        .examples a:hover { text-decoration: underline; }
    </style>
</head>
<body>
    <div class="container">
        <h1>🔍 SQL Search Parser Debugger</h1>

        <div class="input-section">
            <div class="input-group">
                <label for="fhir-url">FHIR URL:</label>
                <input type="text" id="fhir-url" placeholder="/Patient?name=Smith&_sort=-_lastUpdated&_count=10" />
                <button onclick="parseUrl()">Parse</button>
            </div>
            <div class="input-group">
                <label for="continuation-token">Continuation Token:</label>
                <input type="text" id="continuation-token" placeholder="(optional) raw JSON array e.g. [103, 12345]" />
            </div>
            <div class="examples">
                <strong style="color:#6c7086;font-size:13px;">Examples: </strong>
                <a onclick="setExample('/Patient?name=Smith')">Simple search</a>
                <a onclick="setExample('/Patient?_sort=-_lastUpdated&_count=5')">Sort</a>
                <a onclick="setExample('/Patient?_include=Patient:organization')">Include</a>
                <a onclick="setExample('/Patient?_revinclude=Observation:subject')">RevInclude</a>
                <a onclick="setExample('/Patient?organization.name=Acme')">Chain</a>
                <a onclick="setExample('/Patient?_has:Observation:subject:code=test')">Reverse chain</a>
                <a onclick="setExample('/Patient?_include=Patient:organization&_revinclude:iterate=Observation:subject:Organization')">Iterate</a>
                <a onclick="setExample('/Observation?code=http://loinc.org|1234-5&date=ge2024-01-01')">Token+Date</a>
                <a onclick="setExample('/Patient?_not-referenced=Observation:subject')">Not referenced</a>
                <a onclick="setExample('/Patient?name=Smith&_sort=birthdate&_count=5')">Sort+filter</a>
            </div>
        </div>

        <div class="output-section">
            <div class="panel" id="params-panel">
                <h2>📋 Parsed Parameters</h2>
                <div id="params-output"><pre>Enter a FHIR URL and click Parse</pre></div>
            </div>
            <div class="panel" id="info-panel">
                <h2>ℹ️ Query Info</h2>
                <div id="info-output"><pre>Resource type and ID mappings will appear here</pre></div>
            </div>
            <div class="panel full-width" id="sql-panel">
                <h2>🗃️ Generated SQL</h2>
                <div id="sql-output"><pre>SQL output will appear here</pre></div>
            </div>
        </div>
    </div>

    <script>
        function setExample(url) {
            document.getElementById('fhir-url').value = url;
            document.getElementById('continuation-token').value = '';
            parseUrl();
        }

        async function parseUrl() {
            const url = document.getElementById('fhir-url').value.trim();
            const ct = document.getElementById('continuation-token').value.trim();
            if (!url) return;

            try {
                const resp = await fetch('/api/parse', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ url, continuationToken: ct || null })
                });
                const data = await resp.json();

                if (data.error) {
                    document.getElementById('sql-output').innerHTML = `<div class="error"><strong>Error:</strong>\n${escapeHtml(data.error)}\n\n${escapeHtml(data.stackTrace || '')}</div>`;
                    document.getElementById('params-output').innerHTML = '';
                    document.getElementById('info-output').innerHTML = '';
                } else {
                    renderParams(data.queryParameters);
                    renderInfo(data);
                    renderSql(data.formattedSql || data.generatedSql);
                }
            } catch (e) {
                document.getElementById('sql-output').innerHTML = `<div class="error">${escapeHtml(e.message)}</div>`;
            }
        }

        function renderParams(params) {
            if (!params) { document.getElementById('params-output').innerHTML = '<pre>No parameters</pre>'; return; }
            let html = '<table class="params-table"><tr><th>Parameter</th><th>Value(s)</th></tr>';
            for (const [key, values] of Object.entries(params)) {
                const badges = values.map(v => `<span class="badge">${escapeHtml(v)}</span>`).join(' ');
                html += `<tr><td>${escapeHtml(key)}</td><td>${badges}</td></tr>`;
            }
            html += '</table>';
            document.getElementById('params-output').innerHTML = html;
        }

        function renderInfo(data) {
            let html = '<table class="params-table">';
            html += `<tr><td>Resource Type</td><td><span class="badge type">${escapeHtml(data.resourceType)}</span> <span class="badge id">TypeId: ${data.resourceTypeId}</span></td></tr>`;
            if (data.continuationTokenParsed) {
                html += `<tr><td>Continuation Token</td><td><code>${escapeHtml(data.continuationTokenParsed)}</code></td></tr>`;
            }
            html += '</table>';
            document.getElementById('info-output').innerHTML = html;
        }

        function renderSql(sql) {
            if (!sql) {
                document.getElementById('sql-output').innerHTML = '<pre>(no SQL generated)</pre>';
                return;
            }
            document.getElementById('sql-output').innerHTML = `<pre>${highlightSql(escapeHtml(sql))}</pre>`;
        }

        function highlightSql(sql) {
            const keywords = ['WITH', 'AS', 'SELECT', 'FROM', 'WHERE', 'AND', 'OR', 'INNER JOIN', 'LEFT JOIN',
                'ON', 'ORDER BY', 'GROUP BY', 'HAVING', 'UNION ALL', 'UNION', 'TOP', 'DISTINCT',
                'EXISTS', 'NOT EXISTS', 'IN', 'NOT IN', 'CASE', 'WHEN', 'THEN', 'ELSE', 'END',
                'ASC', 'DESC', 'IS NULL', 'IS NOT NULL', 'LIKE', 'BETWEEN', 'OPTION', 'INTERSECT'];
            const functions = ['ROW_NUMBER', 'OVER', 'count_big', 'COALESCE', 'CAST', 'CONVERT', 'ISNULL'];

            let result = sql;
            result = result.replace(/&#x27;([^&#]*(?:&#[^x][^;]*;[^&#]*)*)&#x27;/g, '<span class="sql-string">\'$1\'</span>');
            result = result.replace(/\b(\d+)\b/g, '<span class="sql-number">$1</span>');
            for (const fn of functions) {
                result = result.replace(new RegExp(`\\b(${fn})\\b`, 'gi'), '<span class="sql-function">$1</span>');
            }
            for (const kw of keywords) {
                result = result.replace(new RegExp(`\\b(${kw.replace(' ', '\\s+')})\\b`, 'gi'), '<span class="sql-keyword">$1</span>');
            }
            result = result.replace(/\b(dbo\.\w+)\b/g, '<span class="sql-table">$1</span>');
            result = result.replace(/\b(cte\d+\w*)\b/g, '<span class="sql-column">$1</span>');
            return result;
        }

        function escapeHtml(text) {
            if (!text) return '';
            return text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;').replace(/'/g, '&#x27;');
        }

        document.addEventListener('DOMContentLoaded', () => {
            document.getElementById('fhir-url').addEventListener('keypress', e => { if (e.key === 'Enter') parseUrl(); });
            document.getElementById('continuation-token').addEventListener('keypress', e => { if (e.key === 'Enter') parseUrl(); });
        });
    </script>
</body>
</html>
""";
}
