// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
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
            string includeTop = options.AddParameter(options.IncludeCount + 1, includeInHash: false).ToString();
            string partialThreshold = options.AddParameter(options.IncludeCount, includeInHash: true).ToString();
            sqlBuilder.BeginCte("cte" + options.CteNumber);
            sqlBuilder.SelectWithModifier($"DISTINCT TOP ({includeTop})", "refTarget.ResourceTypeId", "refTarget.ResourceSurrogateId", "0 AS IsMatch", $"CASE WHEN count_big(*) over() > {partialThreshold} THEN 1 ELSE 0 END AS IsPartial");
            sqlBuilder.From("dbo.ReferenceSearchParam", "refSource");
            sqlBuilder.InnerJoin("dbo.Resource", "refTarget", "refSource.ReferenceResourceTypeId = refTarget.ResourceTypeId AND refSource.ReferenceResourceId = refTarget.ResourceId");

            if (options.IsIterateInclude)
            {
                sqlBuilder.Where($"EXISTS (SELECT * FROM {options.LastCteName} lcte WHERE refSource.ResourceTypeId = lcte.ResourceTypeId AND refSource.ResourceSurrogateId = lcte.ResourceSurrogateId)");
            }
            else
            {
                string rowLimit = options.AddParameter(options.Count, includeInHash: true).ToString();
                sqlBuilder.Where($"EXISTS (SELECT * FROM {options.LastCteName} lcte WHERE refSource.ResourceTypeId = lcte.ResourceTypeId AND refSource.ResourceSurrogateId = lcte.ResourceSurrogateId AND lcte.Row <= {rowLimit})");
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

            if (options.IncludesContinuationToken != null && options.IncludesContinuationToken.IncludeResourceTypeId.HasValue && options.IncludesContinuationToken.IncludeResourceSurrogateId.HasValue)
            {
                var includeResourceTypeId = options.AddParameter(VLatest.Resource.ResourceTypeId, options.IncludesContinuationToken.IncludeResourceTypeId.Value, includeInHash: false);
                var includeResourceSurrogateId = options.AddParameter(VLatest.Resource.ResourceSurrogateId, options.IncludesContinuationToken.IncludeResourceSurrogateId.Value, includeInHash: false);
                sqlBuilder.And($"(refTarget.ResourceTypeId > {includeResourceTypeId} OR (refTarget.ResourceTypeId = {includeResourceTypeId} AND refTarget.ResourceSurrogateId > {includeResourceSurrogateId}))");
            }

            sqlBuilder.EndCte();
        }
    }
}
