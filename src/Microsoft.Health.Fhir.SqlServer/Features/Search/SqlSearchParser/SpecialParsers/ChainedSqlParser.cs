// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser
{
    public class ChainedSqlParser : ISqlParser
    {
        private readonly SearchParameterCollection _parameterCollection;
        private readonly Dictionary<string, ISqlParser> _sqlParsers;
        private readonly ISqlServerFhirModel _model;

        public ChainedSqlParser(SearchParameterCollection parameterCollection, Dictionary<string, ISqlParser> sqlParsers, ISqlServerFhirModel model)
        {
            ArgumentNullException.ThrowIfNull(parameterCollection);
            ArgumentNullException.ThrowIfNull(sqlParsers);
            ArgumentNullException.ThrowIfNull(model);

            _parameterCollection = parameterCollection;
            _sqlParsers = sqlParsers;
            _model = model;
        }

        public string? Parse(string name, string value, ParserOptions options)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            // Split the chained parameter name
            var parts = name.Split('.', 2);
            if (parts.Length < 2)
            {
                return null;
            }

            var firstCode = parts[0];
            var remainingChain = parts[1];

            // Look up the first parameter (should be a reference parameter)
            var parameter = _parameterCollection.GetByCode(firstCode, options.ResourceTypes.FirstOrDefault());
            if (parameter == null)
            {
                return null;
            }

            var sqlBuilder = new StringBuilder();

            // Create the first CTE for the reference join
            sqlBuilder.Append($"WITH ctechain{options.ChainLevel}_0 AS (");
            sqlBuilder.AppendLine();
            sqlBuilder.Append("  SELECT refSource.ResourceTypeId AS RefResourceTypeId, refSource.ResourceSurrogateId AS RefResourceSurrogateId, refTarget.ResourceTypeId AS ResourceTypeId, refTarget.ResourceSurrogateId AS ResourceSurrogateId");
            sqlBuilder.AppendLine();
            sqlBuilder.Append("  FROM dbo.ReferenceSearchParam refSource");
            sqlBuilder.AppendLine();
            sqlBuilder.Append("  JOIN dbo.Resource refTarget ON refSource.ReferenceResourceTypeId = refTarget.ResourceTypeId AND refSource.ReferenceResourceId = refTarget.ResourceId");
            sqlBuilder.AppendLine();
            sqlBuilder.Append($"  WHERE refSource.SearchParamId = {parameter.Id}");
            sqlBuilder.AppendLine();
            sqlBuilder.Append("  AND refTarget.IsHistory = 0");
            sqlBuilder.AppendLine();
            sqlBuilder.Append("  AND refTarget.IsDeleted = 0");

            if (options.ContinuationToken != null)
            {
                sqlBuilder.AppendLine($"  AND refSource.ResourceSurrogateId {(options.SortDescending ? "<" : ">")} {options.ContinuationToken.ResourceSurrogateId}");

                if (options.ContinuationToken.ResourceTypeId != null)
                {
                    sqlBuilder.AppendLine($"  AND refSource.ResourceTypeId {(options.SortDescending ? "<" : ">")}= {options.ContinuationToken.ResourceTypeId}");
                }
            }

            sqlBuilder.AppendLine();
            sqlBuilder.Append("),");
            sqlBuilder.AppendLine();

            // Recursively parse the remaining chain
            sqlBuilder.Append("ctechain1_1 AS (");
            sqlBuilder.AppendLine();

            // Get the parameter for the remaining chain to find the right parser
            var remainingParameter = _parameterCollection.GetByCode(remainingChain.Split('.')[0], _model.GetResourceTypeId(parameter.TargetResourceTypes[0]));
            if (remainingParameter == null)
            {
                return null;
            }

            if (!_sqlParsers.TryGetValue(remainingParameter.Type, out var parser))
            {
                return null;
            }

            var chainedSql = parser.Parse(
                remainingChain,
                value,
                new ParserOptions
                {
                    LastCteName = $"ctechain{options.ChainLevel}_0",
                    ChainLevel = options.ChainLevel + 1,
                });

            if (chainedSql == null)
            {
                return null;
            }

            sqlBuilder.Append(chainedSql);
            sqlBuilder.AppendLine();
            sqlBuilder.Append(')');
            sqlBuilder.AppendLine();

            sqlBuilder.AppendLine($"SELECT DISTINCT {(options.IncludeTotalCount ? string.Empty : $"TOP ({options.Count + 1})")} RefResourceTypeId as ResourceTypeId, RefResourceSurrogateId as ResourceSurrogateId, 1 AS IsMatch, 0 AS IsPartial, row_number() OVER (ORDER BY ResourceTypeId ASC, ResourceSurrogateId ASC) AS Row FROM ctechain{options.ChainLevel}_1");

            if (!options.IncludeTotalCount)
            {
                sqlBuilder.AppendLine($"  ORDER BY ResourceTypeId {(options.SortDescending ? "DESC" : "ASC")}, ResourceSurrogateId {(options.SortDescending ? "DESC" : "ASC")}");
            }

            return sqlBuilder.ToString();
        }
    }
}
