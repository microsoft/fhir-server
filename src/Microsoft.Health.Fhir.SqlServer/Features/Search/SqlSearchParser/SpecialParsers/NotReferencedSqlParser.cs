// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser
{
    /// <summary>
    /// Parses _not-referenced search parameters to find resources that are NOT referenced by other resources.
    /// Syntax: &lt;sourceResourceType&gt;:&lt;referenceSearchParameter&gt; where either or both can be wildcards (*).
    /// </summary>
    public class NotReferencedSqlParser : ISqlParser
    {
        private readonly SqlSearchParameterDefinitionManager _parameterCollection;
        private readonly ISqlServerFhirModel _model;

        public NotReferencedSqlParser(SqlSearchParameterDefinitionManager parameterCollection, ISqlServerFhirModel model)
        {
            ArgumentNullException.ThrowIfNull(parameterCollection);
            ArgumentNullException.ThrowIfNull(model);
            _parameterCollection = parameterCollection;
            _model = model;
        }

        public void Parse(string name, string value, ParserOptions options)
        {
            if (string.IsNullOrWhiteSpace(options.LastCteName))
            {
                throw new ArgumentException("LastCteName must be provided in ParserOptions.");
            }

            var parts = value.Split(':');
            if (parts.Length < 2)
            {
                // Invalid format - no separator. This case is handled at the SearchOptionsFactory level
                // which produces a warning. If it reaches here, just skip.
                return;
            }

            var sourceType = parts[0];
            var searchParam = parts[1];

            bool wildcardSourceType = string.Equals(sourceType, "*", StringComparison.OrdinalIgnoreCase);
            bool wildcardSearchParam = string.Equals(searchParam, "*", StringComparison.OrdinalIgnoreCase);

            short sourceResourceTypeId = 0;
            SearchParameterIdWrapper? parameter = null;

            if (!wildcardSourceType)
            {
                sourceResourceTypeId = _model.GetResourceTypeId(sourceType);
            }

            if (!wildcardSearchParam && !wildcardSourceType)
            {
                parameter = _parameterCollection.GetByCode(searchParam, sourceResourceTypeId);
            }

            var sqlBuilder = options.SqlQueryBuilder;
            sqlBuilder.BeginCte($"cte{options.CteNumber}");
            sqlBuilder.Select("r.ResourceTypeId", "r.ResourceSurrogateId");
            sqlBuilder.From(options.LastCteName, "r");
            sqlBuilder.InnerJoin("dbo.Resource", "res", "r.ResourceTypeId = res.ResourceTypeId AND r.ResourceSurrogateId = res.ResourceSurrogateId");

            // Build NOT EXISTS subquery
            var notExistsConditions = new List<string>
            {
                "ref.ReferenceResourceTypeId = res.ResourceTypeId",
                "ref.ReferenceResourceId = res.ResourceId",
            };

            if (!wildcardSourceType)
            {
                notExistsConditions.Add($"ref.ResourceTypeId = {sourceResourceTypeId}");
            }

            if (!wildcardSearchParam && parameter != null)
            {
                notExistsConditions.Add($"ref.SearchParamId = {parameter.Id}");
            }

            var notExistsClause = string.Join(" AND ", notExistsConditions);
            sqlBuilder.Where($"NOT EXISTS (SELECT 1 FROM dbo.ReferenceSearchParam ref WHERE {notExistsClause})");

            ParserUtil.AddFirstCteFilters(sqlBuilder, options, "r");
            sqlBuilder.EndCte();
        }
    }
}
