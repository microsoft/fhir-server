// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser.CompositeParsers;
using Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser.SpecialParsers;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser
{
    public class SearchParameterSqlParser
    {
        private readonly SearchParameterCollection _parameterCollection;
        private readonly Dictionary<SearchParamType, ISqlParser> _sqlParsers;
        private readonly SystemSqlParser _systemSqlParser;
        private readonly IdSqlParser _idSqlParser;
        private readonly ISqlServerFhirModel _sqlServerFhirModel;

        public SearchParameterSqlParser(SearchParameterCollection parameterCollection, ISqlServerFhirModel fhirModel)
        {
            ArgumentNullException.ThrowIfNull(parameterCollection);
            ArgumentNullException.ThrowIfNull(fhirModel);

            _parameterCollection = parameterCollection;
            _sqlServerFhirModel = fhirModel;
            _systemSqlParser = new SystemSqlParser();
            _idSqlParser = new IdSqlParser();
            _sqlParsers = new Dictionary<SearchParamType, ISqlParser>()
            {
                { SearchParamType.Number, new NumberSqlParser(parameterCollection) },
                { SearchParamType.Date, new DateTimeSqlParser(parameterCollection) },
                { SearchParamType.String, new StringSqlParser(parameterCollection) },
                { SearchParamType.Token, new TokenSqlParser(parameterCollection) },
                { SearchParamType.Reference, new ReferenceSqlParser(parameterCollection, fhirModel) },
                { SearchParamType.Uri, new UriSqlParser(parameterCollection) },
                { SearchParamType.Include, new IncludeSqlParser(parameterCollection) },
            };

            _sqlParsers.Add(SearchParamType.Chained, new ChainedSqlParser(parameterCollection, _sqlParsers, fhirModel));
        }

        public string? ParseMultiple(IDictionary<string, IList<string>> parameters, SqlSearchOptions sqlSearchOptions, ContinuationToken? continuationToken = null)
        {
            var parametersCopy = parameters.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            var sqlBuilder = new StringBuilder();
            var cteIndex = 0;
            string? lastCteName = null;
            Dictionary<string, IList<string>> includeParameters = new();
            var parserOptions = new ParserOptions()
            {
                ContinuationToken = continuationToken,
                Count = sqlSearchOptions.MaxItemCount,
            };

            // Extract and process _sort parameter
            string? sortParameterName = null;
            bool sortDescending = false;
            bool sortIsSpecialParameter = false;

            if (parametersCopy.TryGetValue("_sort", out var sortValues) && sortValues.Count > 0)
            {
                var sortValue = sortValues[0]; // Use first sort parameter
                sortDescending = sortValue.StartsWith('-');
                sortParameterName = sortDescending ? sortValue[1..] : sortValue;

                // Check if this is a special parameter (_lastUpdated or _type)
                sortIsSpecialParameter = sortParameterName.Equals(SearchParameterNames.LastUpdated, StringComparison.OrdinalIgnoreCase) ||
                                        sortParameterName.Equals(SearchParameterNames.ResourceType, StringComparison.OrdinalIgnoreCase);

                parserOptions.SortParameterName = sortParameterName;
                parserOptions.SortDescending = sortDescending;
                parserOptions.SortIsSpecialParameter = sortIsSpecialParameter;
                parserOptions.SortQuerySecondPhase = sqlSearchOptions.SortQuerySecondPhase;

                parametersCopy.Remove("_sort");
            }

            parametersCopy = parametersCopy.Where(param =>
            {
                if (param.Key.Equals("_elements", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                return true;
            }).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            // Check for _summary=accurate parameter
            if (parametersCopy.TryGetValue("_summary", out var summaryValues))
            {
                if (summaryValues.Any(v => v.Equals("count", StringComparison.OrdinalIgnoreCase)))
                {
                    parserOptions.IncludeTotalCount = true;
                }

                parametersCopy.Remove("_summary");
            }

            if (parametersCopy.TryGetValue("_count", out var countValues))
            {
                parametersCopy.Remove("_count");
            }

            if (parametersCopy.TryGetValue("_type", out var typeValues))
            {
                foreach (var typeValue in typeValues.SelectMany(types => types.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).Select(_sqlServerFhirModel.GetResourceTypeId))
                {
                    parserOptions.ResourceTypes.Add(typeValue);
                }

                parametersCopy.Remove("_type");
            }

            sqlBuilder.AppendLine("DECLARE @FilteredData AS TABLE (T1 smallint, Sid1 bigint, IsMatch bit, IsPartial bit)");
            sqlBuilder.AppendLine(";WITH");

            // If no search parameters, use SystemSqlParser for basic resource retrieval
            if (parametersCopy.Count == 0)
            {
                lastCteName = $"cte{cteIndex}";
                cteIndex++;

                sqlBuilder.AppendLine($"{lastCteName} AS (");

                sqlBuilder.Append(_systemSqlParser.Parse(string.Empty, string.Empty, parserOptions));

                sqlBuilder.AppendLine();
                sqlBuilder.Append(')');
            }
            else
            {
                foreach (var kvp in parametersCopy)
                {
                    // Handle _id parameter specially - it doesn't use SearchParameterCollection
                    if (kvp.Key.Equals("_id", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (var value in kvp.Value)
                        {
                            var cteName = $"cte{cteIndex}";

                            if (cteIndex > 0)
                            {
                                sqlBuilder.Append(',');
                            }

                            sqlBuilder.AppendLine($"{cteName} AS (");
                            sqlBuilder.Append(_idSqlParser.Parse(kvp.Key, value, parserOptions));
                            sqlBuilder.AppendLine();
                            sqlBuilder.Append(')');

                            lastCteName = cteName;
                            cteIndex++;
                            parserOptions.LastCteName = lastCteName;
                        }

                        continue;
                    }

                    if (_sqlParsers.TryGetValue(_parameterCollection.GetParameterType(kvp.Key, parserOptions.ResourceTypes.FirstOrDefault()) ?? string.Empty, out var parser) && parser is IncludeSqlParser)
                    {
                        includeParameters.Add(kvp.Key, kvp.Value);
                        continue;
                    }

                    foreach (var value in kvp.Value)
                    {
                        var parameter = _parameterCollection.GetByCode(kvp.Key, parserOptions.ResourceTypes.FirstOrDefault());
                        if (parameter == null)
                        {
                            continue;
                        }

                        var cteName = $"cte{cteIndex}";

                        if (cteIndex > 0)
                        {
                            sqlBuilder.Append(',');
                        }

                        sqlBuilder.AppendLine($"{cteName} AS (");

                        sqlBuilder.Append(Parse(kvp.Key, value, parserOptions));

                        sqlBuilder.AppendLine();
                        sqlBuilder.Append(')');

                        lastCteName = cteName;
                        cteIndex++;
                        parserOptions.LastCteName = lastCteName;
                    }
                }
            }

            if (lastCteName == null)
            {
                return null;
            }

            if (!parserOptions.IncludeTotalCount)
            {
                var cteName = $"cte{cteIndex}";
                cteIndex++;

                sqlBuilder.AppendLine($",{cteName} AS (")
                    .AppendLine($"SELECT TOP {parserOptions.Count + 1} * FROM {lastCteName} r")
                    .AppendLine($"  ORDER BY r.ResourceTypeId {(parserOptions.SortDescending ? "DESC" : "ASC")}, r.ResourceSurrogateId {(parserOptions.SortDescending ? "DESC" : "ASC")}")
                    .AppendLine(")");

                lastCteName = cteName;
            }

            if (includeParameters.Count > 0 && !parserOptions.IncludeTotalCount)
            {
                var baseCteName = $"cte{cteIndex}";
                cteIndex++;

                sqlBuilder.AppendLine($"INSERT INTO @FilteredData SELECT T1, Sid1, IsMatch = 1, IsPartial = 0 FROM {lastCteName}");
                sqlBuilder.AppendLine($"; WITH {baseCteName} AS(SELECT * FROM @FilteredData)");

                parserOptions.LastCteName = baseCteName;

                var includeCteNames = new List<string>();

                foreach (var kvp in includeParameters)
                {
                    foreach (var value in kvp.Value)
                    {
                        var includeCteName = $"cte{cteIndex}";
                        cteIndex++;

                        sqlBuilder.AppendLine($",{includeCteName} AS (");
                        var includeSql = Parse(kvp.Key, value, parserOptions);
                        sqlBuilder.Append(includeSql);
                        sqlBuilder.AppendLine();
                        sqlBuilder.Append(')');

                        includeCteNames.Add(includeCteName);
                    }
                }

                sqlBuilder.AppendLine();
                sqlBuilder.Append($"SELECT * FROM {baseCteName}");

                foreach (var includeCteName in includeCteNames)
                {
                    sqlBuilder.AppendLine();
                    sqlBuilder.Append("UNION ALL");
                    sqlBuilder.AppendLine();
                    sqlBuilder.Append($"SELECT * FROM {includeCteName}");
                }

                return sqlBuilder.ToString();
            }

            sqlBuilder.AppendLine();

            // If this is a count query, return count instead of full results
            if (parserOptions.IncludeTotalCount)
            {
                sqlBuilder.AppendLine($"SELECT COUNT_BIG(*) AS Total FROM {lastCteName}");
            }
            else
            {
                // Build the ORDER BY clause based on sort parameters
                string orderByClause;

                if (!string.IsNullOrEmpty(sortParameterName))
                {
                    if (sortIsSpecialParameter)
                    {
                        // Special parameters map directly to Resource table columns
                        if (sortParameterName.Equals(SearchParameterNames.LastUpdated, StringComparison.OrdinalIgnoreCase))
                        {
                            // _lastUpdated maps to ResourceSurrogateId (which encodes timestamp)
                            orderByClause = sortDescending
                                ? "ORDER BY t.IsMatch DESC, t.ResourceSurrogateId DESC"
                                : "ORDER BY t.IsMatch DESC, t.ResourceSurrogateId ASC";
                        }
                        else if (sortParameterName.Equals(SearchParameterNames.ResourceType, StringComparison.OrdinalIgnoreCase))
                        {
                            // _type maps to ResourceTypeId
                            orderByClause = sortDescending
                                ? "ORDER BY t.IsMatch DESC, t.ResourceTypeId DESC, t.ResourceSurrogateId DESC"
                                : "ORDER BY t.IsMatch DESC, t.ResourceTypeId ASC, t.ResourceSurrogateId ASC";
                        }
                        else
                        {
                            // Fallback to default ordering
                            orderByClause = "ORDER BY t.IsMatch DESC, (CASE WHEN t.IsMatch = 1 THEN t.ResourceTypeId ELSE NULL END) ASC, (CASE WHEN t.IsMatch = 1 THEN t.ResourceSurrogateId ELSE NULL END) ASC, (CASE WHEN t.IsMatch = 0 THEN t.ResourceTypeId ELSE NULL END) ASC, (CASE WHEN t.IsMatch = 0 THEN t.ResourceSurrogateId ELSE NULL END) ASC";
                        }
                    }
                    else
                    {
                        // Regular search parameters - use two-phase approach
                        // Phase 1 (when SortQuerySecondPhase = false): Resources WITHOUT the sort parameter
                        // Phase 2 (when SortQuerySecondPhase = true): Resources WITH the sort parameter

                        if (sortDescending)
                        {
                            // Descending: first show resources WITH values (phase 2), then WITHOUT (phase 1)
                            if (sqlSearchOptions.SortQuerySecondPhase)
                            {
                                // Phase 2: Resources with values, sorted descending by IsMatch and then by value
                                orderByClause = "ORDER BY t.IsMatch DESC, t.ResourceTypeId ASC, t.ResourceSurrogateId DESC";
                            }
                            else
                            {
                                // Phase 1: Resources without values (missing the search parameter)
                                orderByClause = "ORDER BY t.IsMatch DESC, t.ResourceTypeId ASC, t.ResourceSurrogateId DESC";
                            }
                        }
                        else
                        {
                            // Ascending: first show resources WITHOUT values (phase 1), then WITH (phase 2)
                            if (sqlSearchOptions.SortQuerySecondPhase)
                            {
                                // Phase 2: Resources with values, sorted ascending by IsMatch and then by value
                                orderByClause = "ORDER BY t.IsMatch DESC, t.ResourceTypeId ASC, t.ResourceSurrogateId ASC";
                            }
                            else
                            {
                                // Phase 1: Resources without values (missing the search parameter)
                                orderByClause = "ORDER BY t.IsMatch DESC, t.ResourceTypeId ASC, t.ResourceSurrogateId ASC";
                            }
                        }
                    }
                }
                else
                {
                    // No sort parameter - use default ordering
                    orderByClause = "ORDER BY t.IsMatch DESC, (CASE WHEN t.IsMatch = 1 THEN t.ResourceTypeId ELSE NULL END) ASC, (CASE WHEN t.IsMatch = 1 THEN t.ResourceSurrogateId ELSE NULL END) ASC, (CASE WHEN t.IsMatch = 0 THEN t.ResourceTypeId ELSE NULL END) ASC, (CASE WHEN t.IsMatch = 0 THEN t.ResourceSurrogateId ELSE NULL END) ASC";
                }

                sqlBuilder.AppendLine($"SELECT * FROM (")
                    .AppendLine("SELECT DISTINCT r.ResourceTypeId, r.ResourceId, r.Version, r.IsDeleted, r.ResourceSurrogateId, r.RequestMethod, CAST(IsMatch AS bit) AS IsMatch, CAST(IsPartial AS bit) AS IsPartial, r.IsRawResourceMetaSet, r.SearchParamHash, r.RawResource ")
                    .AppendLine("FROM dbo.Resource AS r ")
                    .AppendLine($"JOIN {lastCteName} AS f ON r.ResourceTypeId = f.ResourceTypeId AND r.ResourceSurrogateId = f.ResourceSurrogateId ")
                    .AppendLine("WHERE r.IsHistory = 0 AND r.IsDeleted = 0 ")
                    .AppendLine($") AS t {orderByClause}");
            }

            return sqlBuilder.ToString();
        }

        private string? Parse(string name, string value, ParserOptions options)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            var parameter = _parameterCollection.GetByCode(name, options.ResourceTypes[0]);
            if (parameter == null)
            {
                return null;
            }

            ISqlParser? parser = null;
            if (parameter.SearchParameterInfo.Type == SearchParamType.Composite)
            {
                BaseCompositeSqlParser.DetermineCompositeType(parameter.SearchParameterInfo, _parameterCollection, options.ResourceTypes[0]);
            }
            else if (!_sqlParsers.TryGetValue(parameter.SearchParameterInfo.Type, out parser))
            {
                return null;
            }

            return parser?.Parse(name, value, options);
        }
    }
}
