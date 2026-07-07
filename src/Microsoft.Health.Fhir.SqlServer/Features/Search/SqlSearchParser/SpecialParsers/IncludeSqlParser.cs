// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System;
using System.Text;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser
{
    public class IncludeSqlParser : ISqlParser
    {
        private readonly SqlSearchParameterDefinitionManager _parameterCollection;

        public IncludeSqlParser(SqlSearchParameterDefinitionManager parameterCollection)
        {
            ArgumentNullException.ThrowIfNull(parameterCollection);
            _parameterCollection = parameterCollection;
        }

        public string? Parse(string name, string value, ParserOptions options)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            var parts = value.Split(':', 2);
            SearchParameterIdWrapper? parameter = null;
            var wildcard = false;

            if (!string.Equals(parts[1], "*", StringComparison.OrdinalIgnoreCase))
            {
                parameter = _parameterCollection.GetByCode(parts[1], options.ResourceTypes[0]);
            }
            else
            {
                wildcard = true;
            }

            if (parameter == null && !wildcard)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(options.LastCteName))
            {
                return null;
            }

            var sqlBuilder = new StringBuilder();
            sqlBuilder.Append($"SELECT DISTINCT {(options.IncludeTotalCount ? string.Empty : "TOP (1001) ")}refTarget.ResourceTypeId, refTarget.ResourceSurrogateId, 0 AS IsMatch, CASE WHEN count_big(*) over() > 1000 THEN 1 ELSE 0 END AS IsPartial, row_number() OVER (ORDER BY refTarget.ResourceTypeId ASC, refTarget.ResourceSurrogateId ASC) AS Row");
            sqlBuilder.AppendLine("  FROM dbo.ReferenceSearchParam refSource");
            sqlBuilder.AppendLine("  JOIN dbo.Resource refTarget ON refSource.ReferenceResourceTypeId = refTarget.ResourceTypeId AND refSource.ReferenceResourceId = refTarget.ResourceId");
            sqlBuilder.Append($"  WHERE EXISTS (SELECT * FROM {options.LastCteName} lcte WHERE refSource.ResourceTypeId = lcte.ResourceTypeId AND refSource.ResourceSurrogateId = lcte.ResourceSurrogateId {(options.IncludeTotalCount ? string.Empty : $"AND lcte.Row < {options.Count}")})");

            if (!wildcard)
            {
                sqlBuilder.AppendLine($" AND refSource.SearchParamId = {parameter?.Id}");
            }

            sqlBuilder.AppendLine("  AND refTarget.IsHistory = 0");
            sqlBuilder.AppendLine("  AND refTarget.IsDeleted = 0");

            return sqlBuilder.ToString();
        }
    }
}
