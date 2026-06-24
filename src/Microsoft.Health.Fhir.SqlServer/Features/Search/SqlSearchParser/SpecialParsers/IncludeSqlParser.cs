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
        private readonly SearchParameterCollection _parameterCollection;

        public IncludeSqlParser(SearchParameterCollection parameterCollection)
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
            var parameter = _parameterCollection.GetByCode(parts[1], options.ResourceTypes[0]);

            if (parameter == null)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(options.LastCteName))
            {
                return null;
            }

            var sqlBuilder = new StringBuilder();
            sqlBuilder.Append($"SELECT DISTINCT {(options.IncludeTotalCount ? string.Empty : "TOP (1001) ")}refTarget.ResourceTypeId AS T1, refTarget.ResourceSurrogateId AS Sid1, 0 AS IsMatch, CASE WHEN count_big(*) over() > 1000 THEN 1 ELSE 0 END AS IsPartial");
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
            sqlBuilder.AppendLine();
            sqlBuilder.Append($"  AND EXISTS (SELECT * FROM {options.LastCteName} lcte WHERE refSource.ResourceTypeId = lcte.ResourceTypeId AND refSource.ResourceSurrogateId = lcte.ResourceSurrogateId {(options.IncludeTotalCount ? string.Empty : $"AND lcte.Row < {options.Count}")})");

            return sqlBuilder.ToString();
        }
    }
}
