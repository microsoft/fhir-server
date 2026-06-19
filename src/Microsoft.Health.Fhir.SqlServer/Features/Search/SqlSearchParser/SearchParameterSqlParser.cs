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
using Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser.SpecialParsers;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser
{
    public class SearchParameterSqlParser
    {
        private readonly SearchParameterCollection _parameterCollection;
        private readonly Dictionary<string, ISqlParser> _sqlParsers;
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
            _sqlParsers = new Dictionary<string, ISqlParser>(StringComparer.OrdinalIgnoreCase)
            {
                { "NumberSearchParam", new NumberSqlParser(parameterCollection) },
                { "DateTimeSearchParam", new DateTimeSqlParser(parameterCollection) },
                { "StringSearchParam", new StringSqlParser(parameterCollection) },
                { "TokenSearchParam", new TokenSqlParser(parameterCollection) },
                { "ReferenceSearchParam", new ReferenceSqlParser(parameterCollection, fhirModel) },
                { "UriSearchParam", new UriSqlParser(parameterCollection) },
                { "include", new IncludeSqlParser(parameterCollection) },
            };

            _sqlParsers.Add("chained", new ChainedSqlParser(parameterCollection, _sqlParsers));
        }

        public string? ParseMultiple(IDictionary<string, IList<string>> parameters, long? continuationSurrogateId = null)
        {
            var parametersCopy = parameters.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            var sqlBuilder = new StringBuilder();
            var cteIndex = 0;
            string? lastCteName = null;
            Dictionary<string, IList<string>> includeParameters = new();
            var parserOptions = new ParserOptions()
            {
                ContinuationSurrogateId = continuationSurrogateId,
            };

            parametersCopy = parametersCopy.Where(param =>
            {
                if (param.Key.Equals("_elements", StringComparison.OrdinalIgnoreCase) || param.Key.Equals("_sort", StringComparison.OrdinalIgnoreCase))
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

            if (parametersCopy.TryGetValue("_count", out var countValues) && int.TryParse(countValues.FirstOrDefault(), out var count))
            {
                parserOptions.Count = count;
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

                    if (_sqlParsers.TryGetValue(_parameterCollection.GetParameterType(kvp.Key) ?? string.Empty, out var parser) && parser is IncludeSqlParser)
                    {
                        includeParameters.Add(kvp.Key, kvp.Value);
                        continue;
                    }

                    foreach (var value in kvp.Value)
                    {
                        var parameter = _parameterCollection.GetByCode(kvp.Key);
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

            if (includeParameters.Count > 0)
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
                sqlBuilder.AppendLine("SELECT * FROM (")
                    .AppendLine("SELECT DISTINCT r.ResourceTypeId, r.ResourceId, r.Version, r.IsDeleted, r.ResourceSurrogateId, r.RequestMethod, CAST(IsMatch AS bit) AS IsMatch, CAST(IsPartial AS bit) AS IsPartial, r.IsRawResourceMetaSet, r.SearchParamHash, r.RawResource ")
                    .AppendLine("FROM dbo.Resource AS r ")
                    .AppendLine($"JOIN {lastCteName} AS f ON r.ResourceTypeId = f.ResourceTypeId AND r.ResourceSurrogateId = f.ResourceSurrogateId ")
                    .AppendLine("WHERE r.IsHistory = 0 AND r.IsDeleted = 0 ")
                    .AppendLine(") AS t ORDER BY t.IsMatch DESC, (CASE WHEN t.IsMatch = 1 THEN t.ResourceTypeId ELSE NULL END) ASC, (CASE WHEN t.IsMatch = 1 THEN t.ResourceSurrogateId ELSE NULL END) ASC, (CASE WHEN t.IsMatch = 0 THEN t.ResourceTypeId ELSE NULL END) ASC, (CASE WHEN t.IsMatch = 0 THEN t.ResourceSurrogateId ELSE NULL END) ASC");
            }

            return sqlBuilder.ToString();
        }

        private string? Parse(string name, string value, ParserOptions options)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            var parameterType = _parameterCollection.GetParameterType(name);
            if (parameterType == null)
            {
                return null;
            }

            if (!_sqlParsers.TryGetValue(parameterType, out var parser))
            {
                return null;
            }

            return parser.Parse(name, value, options);
        }
    }
}
