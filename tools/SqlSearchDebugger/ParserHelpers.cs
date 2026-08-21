// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Search;
using Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser;
using SqlSearchDebugger.Mocks;

namespace SqlSearchDebugger;

static class ParserHelpers
{
    public static SearchParameterDefinitionManager InitializeSearchParameterDefinitionManager(IModelInfoProvider modelInfoProvider)
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

    public static object ParseFhirUrl(string url, string? continuationToken, SearchParameterSqlParser parser, FakeSqlServerFhirModel fhirModel)
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

    public static string? FormatSql(string? sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return null;
        }

        return sql;
    }
}

record ParseRequest(string Url, string? ContinuationToken);
