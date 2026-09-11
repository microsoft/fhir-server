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
using Microsoft.Health.SqlServer.Features.Storage;
using System.Globalization;
using System.Text.Json;
using SqlCommand = Microsoft.Data.SqlClient.SqlCommand;
using SqlParameter = Microsoft.Data.SqlClient.SqlParameter;
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

        ContinuationToken? ct = ParseContinuationToken(continuationToken);

        // Generate SQL
        using var command = new SqlCommand();
        var parameterManager = new HashingSqlQueryParameterManager(new SqlQueryParameterManager(command.Parameters));
        var sql = parser.ParseMultiple(parameters, sqlSearchOptions, parameterManager, reuseQueryPlans: true, ct);

        return new
        {
            resourceType,
            resourceTypeId,
            queryParameters = parameters,
            continuationTokenParsed = ct?.ToString(),
            generatedSql = sql,
            formattedSql = FormatSql(sql),
            sqlParameters = command.Parameters.Cast<SqlParameter>().ToDictionary(parameter => parameter.ParameterName, parameter => parameter.Value),
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

    internal static ContinuationToken? ParseContinuationToken(string? continuationToken)
    {
        if (string.IsNullOrWhiteSpace(continuationToken))
        {
            return null;
        }

        ContinuationToken? parsedToken = ParseRawContinuationToken(continuationToken);
        if (parsedToken != null)
        {
            return parsedToken;
        }

        string decodedToken = ContinuationTokenEncoder.Decode(continuationToken);
        return ParseRawContinuationToken(decodedToken)
            ?? throw new BadRequestException("Invalid continuation token.");
    }

    private static ContinuationToken? ParseRawContinuationToken(string continuationToken)
    {
        if (long.TryParse(continuationToken, NumberStyles.None, CultureInfo.InvariantCulture, out _))
        {
            return ContinuationToken.FromString(continuationToken);
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(continuationToken);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            JsonElement[] values = root.EnumerateArray().ToArray();
            if (values.Length is < 1 or > 3 || !IsInt64(values[^1]))
            {
                return null;
            }

            bool validPrefix = values.Length switch
            {
                1 => true,
                2 => IsSortValue(values[0]) || IsInt16(values[0]),
                3 => IsSortValue(values[0]) && IsInt16(values[1]),
                _ => false,
            };

            return validPrefix ? ContinuationToken.FromString(continuationToken) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsSortValue(JsonElement value) =>
        value.ValueKind is JsonValueKind.String or JsonValueKind.Null;

    private static bool IsInt16(JsonElement value) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetInt16(out _);

    private static bool IsInt64(JsonElement value) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _);
}

record ParseRequest(string Url, string? ContinuationToken);
