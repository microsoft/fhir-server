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
    /// <summary>
    /// Parses _revinclude search parameters to find resources that reference the matched resources.
    /// This is the reverse of _include which finds resources referenced by the matched resources.
    /// </summary>
    public class RevIncludeSqlParser : ISqlParser
    {
        private readonly SqlSearchParameterDefinitionManager _parameterCollection;
        private readonly ISqlServerFhirModel _model;

        public RevIncludeSqlParser(SqlSearchParameterDefinitionManager parameterCollection, ISqlServerFhirModel model)
        {
            ArgumentNullException.ThrowIfNull(parameterCollection);
            ArgumentNullException.ThrowIfNull(model);
            _parameterCollection = parameterCollection;
            _model = model;
        }

        public void Parse(string name, string value, ParserOptions options)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentNullException(nameof(name));
            }

            var parts = value.Split(':');
            SearchParameterIdWrapper? parameter = null;
            var wildcardResourceType = false;
            var wildcardSearchParameter = false;
            short resourceTypeId = 0;
            var targetResourceTypeIds = new List<short>();

            // Parse the resource type that will reference our matched resources
            if (!string.Equals(parts[0], "*", StringComparison.OrdinalIgnoreCase))
            {
                resourceTypeId = _model.GetResourceTypeId(parts[0]);
            }
            else
            {
                wildcardResourceType = true;
            }

            // Parse the search parameter on the referencing resource
            if (parts.Length > 1 && !string.Equals(parts[1], "*", StringComparison.OrdinalIgnoreCase))
            {
                parameter = _parameterCollection.GetByCode(parts[1], resourceTypeId);
            }
            else
            {
                wildcardSearchParameter = true;
            }

            // Parse target resource types (the matched resources)
            if (parts.Length > 2)
            {
                var targetResourceTypes = parts[2].Split(',');
                targetResourceTypeIds = targetResourceTypes.Select(x => _model.GetResourceTypeId(x)).ToList();
            }

            if (parameter == null && !wildcardSearchParameter)
            {
                throw new ArgumentException($"Search parameter '{parts[1]}' not found for resource type '{parts[0]}'.");
            }

            if (string.IsNullOrWhiteSpace(options.LastCteName))
            {
                throw new ArgumentException("LastCteName must be provided in ParserOptions.");
            }

            var sqlBuilder = options.SqlQueryBuilder;
            sqlBuilder.BeginCte($"cte{options.CteNumber}");
            sqlBuilder.AppendLine("SELECT *, row_number() OVER (ORDER BY ResourceTypeId ASC, ResourceSurrogateId ASC) AS Row");
            sqlBuilder.AppendLine("  FROM (");
            sqlBuilder.AppendLine($"    SELECT DISTINCT TOP (1001) refSource.ResourceTypeId, refSource.ResourceSurrogateId, 0 AS IsMatch, CASE WHEN count_big(*) over() > 1000 THEN 1 ELSE 0 END AS IsPartial");
            sqlBuilder.AppendLine("      FROM dbo.ReferenceSearchParam refSource");
            sqlBuilder.AppendLine("        JOIN dbo.Resource refTarget ON refSource.ReferenceResourceTypeId = refTarget.ResourceTypeId AND refSource.ReferenceResourceId = refTarget.ResourceId");
            sqlBuilder.AppendLine($"      WHERE EXISTS (SELECT * FROM {options.LastCteName} lcte WHERE refTarget.ResourceTypeId = lcte.ResourceTypeId AND refTarget.ResourceSurrogateId = lcte.ResourceSurrogateId AND lcte.Row <= {options.Count})");

            // For revinclude, we want resources (refSource) that reference the matched resources (refTarget)
            // So we filter on refSource's ResourceTypeId (the referencing resource type)
            if (!wildcardResourceType)
            {
                sqlBuilder.AppendLine($"        AND refSource.ResourceTypeId = {resourceTypeId}");
            }

            if (!wildcardSearchParameter)
            {
                sqlBuilder.AppendLine($"        AND refSource.SearchParamId = {parameter?.Id}");
            }

            // Filter on the target resource type if specified (the matched resources that are being referenced)
            if (targetResourceTypeIds.Count > 0)
            {
                sqlBuilder.AppendLine($"        AND refTarget.ResourceTypeId IN ({string.Join(",", targetResourceTypeIds)})");
            }

            sqlBuilder.AppendLine("  ) AS a");
            sqlBuilder.EndCte();
        }
    }
}
