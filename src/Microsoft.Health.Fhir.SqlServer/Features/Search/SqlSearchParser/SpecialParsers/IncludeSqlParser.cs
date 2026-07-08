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
    public class IncludeSqlParser : ISqlParser
    {
        private readonly SqlSearchParameterDefinitionManager _parameterCollection;
        private readonly ISqlServerFhirModel _model;

        public IncludeSqlParser(SqlSearchParameterDefinitionManager parameterCollection, ISqlServerFhirModel model)
        {
            ArgumentNullException.ThrowIfNull(parameterCollection);
            ArgumentNullException.ThrowIfNull(model);
            _parameterCollection = parameterCollection;
            _model = model;
        }

        public string? Parse(string name, string value, ParserOptions options)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            var parts = value.Split(':');
            SearchParameterIdWrapper? parameter = null;
            var wildcardResourceType = false;
            var wildcardSearchParameter = false;
            short resourceTypeId = 0;
            var targetResourceTypeIds = new List<short>();

            if (!string.Equals(parts[0], "*", StringComparison.OrdinalIgnoreCase))
            {
                resourceTypeId = _model.GetResourceTypeId(parts[0]);
            }
            else
            {
                wildcardResourceType = true;
            }

            if (parts.Length > 1 && !string.Equals(parts[1], "*", StringComparison.OrdinalIgnoreCase))
            {
                parameter = _parameterCollection.GetByCode(parts[1], resourceTypeId);
            }
            else
            {
                wildcardSearchParameter = true;
            }

            if (parts.Length > 2)
            {
                var targetResourceTypes = parts[2].Split(',');
                targetResourceTypeIds = targetResourceTypes.Select(x => _model.GetResourceTypeId(x)).ToList();
            }

            if (parameter == null && !wildcardSearchParameter)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(options.LastCteName))
            {
                return null;
            }

            var sqlBuilder = new StringBuilder();
            sqlBuilder.AppendLine("SELECT *, row_number() OVER (ORDER BY ResourceTypeId ASC, ResourceSurrogateId ASC) AS Row");
            sqlBuilder.AppendLine("  FROM (");
            sqlBuilder.AppendLine($"    SELECT DISTINCT {(options.IncludeTotalCount ? string.Empty : "TOP (1001) ")}refTarget.ResourceTypeId, refTarget.ResourceSurrogateId, 0 AS IsMatch, CASE WHEN count_big(*) over() > 1000 THEN 1 ELSE 0 END AS IsPartial");
            sqlBuilder.AppendLine("      FROM dbo.ReferenceSearchParam refSource");
            sqlBuilder.AppendLine("        JOIN dbo.Resource refTarget ON refSource.ReferenceResourceTypeId = refTarget.ResourceTypeId AND refSource.ReferenceResourceId = refTarget.ResourceId");
            sqlBuilder.AppendLine($"      WHERE EXISTS (SELECT * FROM {options.LastCteName} lcte WHERE refSource.ResourceTypeId = lcte.ResourceTypeId AND refSource.ResourceSurrogateId = lcte.ResourceSurrogateId {(options.IncludeTotalCount ? string.Empty : $"AND lcte.Row <= {options.Count}")})");

            if (!wildcardResourceType)
            {
                sqlBuilder.AppendLine($"        AND refSource.ResourceTypeId = {resourceTypeId}");
            }

            if (!wildcardSearchParameter)
            {
                sqlBuilder.AppendLine($"       AND refSource.SearchParamId = {parameter?.Id}");
            }

            if (targetResourceTypeIds.Count > 0)
            {
                sqlBuilder.AppendLine($"       AND refTarget.ResourceTypeId IN ({string.Join(",", targetResourceTypeIds)})");
            }

            sqlBuilder.AppendLine("        AND refTarget.IsHistory = 0");
            sqlBuilder.AppendLine("        AND refTarget.IsDeleted = 0");
            sqlBuilder.AppendLine("  ) AS a");

            return sqlBuilder.ToString();
        }
    }
}
