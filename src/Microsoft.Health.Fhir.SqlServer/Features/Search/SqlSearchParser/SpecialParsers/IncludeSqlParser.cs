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
                throw new ArgumentException("No search parameter found for the given resource type and code.", nameof(value));
            }

            if (string.IsNullOrWhiteSpace(options.LastCteName))
            {
                throw new ArgumentException("LastCteName cannot be null or whitespace.");
            }

            var sqlBuilder = options.SqlQueryBuilder;
            sqlBuilder.BeginCte("cte" + options.CteNumber);
            sqlBuilder.Select("refTarget.ResourceTypeId", "refTarget.ResourceSurrogateId", "0 AS IsMatch", "CASE WHEN count_big(*) over() > 1000 THEN 1 ELSE 0 END AS IsPartial");
            sqlBuilder.From("dbo.ReferenceSearchParam", "refSource");
            sqlBuilder.InnerJoin("dbo.Resource", "refTarget", "refSource.ReferenceResourceTypeId = refTarget.ResourceTypeId AND refSource.ReferenceResourceId = refTarget.ResourceId");

            if (options.IsIterateInclude)
            {
                sqlBuilder.Where($"EXISTS (SELECT * FROM {options.LastCteName} lcte WHERE refSource.ResourceTypeId = lcte.ResourceTypeId AND refSource.ResourceSurrogateId = lcte.ResourceSurrogateId)");
            }
            else
            {
                sqlBuilder.Where($"EXISTS (SELECT * FROM {options.LastCteName} lcte WHERE refSource.ResourceTypeId = lcte.ResourceTypeId AND refSource.ResourceSurrogateId = lcte.ResourceSurrogateId AND lcte.Row <= {options.Count})");
            }

            if (!wildcardResourceType)
            {
                sqlBuilder.And($"refSource.ResourceTypeId = {resourceTypeId}");
            }

            if (!wildcardSearchParameter)
            {
                sqlBuilder.And($"refSource.SearchParamId = {parameter?.Id}");
            }

            if (targetResourceTypeIds.Count > 0)
            {
                sqlBuilder.And($"refTarget.ResourceTypeId IN ({string.Join(",", targetResourceTypeIds)})");
            }

            ParserUtil.AddHistoryAndDeletedCheck(sqlBuilder, "refTarget");
            sqlBuilder.EndCte();
        }
    }
}
