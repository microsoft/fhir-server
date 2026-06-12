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

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser
{
    public class SearchParameterSqlParser
    {
        private readonly SearchParameterCollection _parameterCollection;
        private readonly Dictionary<string, ISqlParser> _sqlParsers;

        public SearchParameterSqlParser(SearchParameterCollection parameterCollection)
        {
            ArgumentNullException.ThrowIfNull(parameterCollection);

            _parameterCollection = parameterCollection;
            _sqlParsers = new Dictionary<string, ISqlParser>(StringComparer.OrdinalIgnoreCase)
            {
                { "number", new NumberSqlParser(parameterCollection) },
                { "date", new DateTimeSqlParser(parameterCollection) },
                { "string", new StringSqlParser(parameterCollection) },
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
                foreach (var typeValue in typeValues.Select(int.Parse))
                {
                    parserOptions.ResourceTypes.Add(typeValue);
                }

                parametersCopy.Remove("_type");
            }

            sqlBuilder.AppendLine("DECLARE @FilteredData AS TABLE (T1 smallint, Sid1 bigint, IsMatch bit, IsPartial bit)");
            sqlBuilder.AppendLine(";WITH");

            foreach (var kvp in parametersCopy)
            {
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
                sqlBuilder.Append($"SELECT COUNT(*) AS Total FROM {lastCteName}");
            }
            else
            {
                sqlBuilder.Append($"SELECT * FROM {lastCteName}");
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
