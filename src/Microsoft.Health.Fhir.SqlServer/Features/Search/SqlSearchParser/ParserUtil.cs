// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Search;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser
{
    internal class ParserUtil
    {
        public static void AddHistoryAndDeletedCheck(SqlQueryBuilder builder, string tableAlias, bool includeHistory = false, bool includeDeleted = false)
        {
            if (!includeHistory)
            {
                builder.And($"{tableAlias}.IsHistory = 0");
            }

            if (!includeDeleted)
            {
                builder.And($"{tableAlias}.IsDeleted = 0");
            }
        }

        public static void AddFirstCteFilters(SqlQueryBuilder builder, ParserOptions options, string tableAlias)
        {
            // Add base filters only on the first CTE
            if (options.LastCteName == null)
            {
                AddHistoryAndDeletedCheck(builder, tableAlias, options.ResourceVersionType.HasFlag(ResourceVersionType.History), options.ResourceVersionType.HasFlag(ResourceVersionType.SoftDeleted));
                if (options.ResourceTypes != null && options.ResourceTypes.Count > 0)
                {
                    var resourceTypeIds = string.Join(", ", options.ResourceTypes);
                    builder.And($"{tableAlias}.ResourceTypeId IN ({resourceTypeIds})");
                }

                if (options.ExcludedResourceTypes != null && options.ExcludedResourceTypes.Count > 0)
                {
                    var excludedResourceTypeIds = string.Join(", ", options.ExcludedResourceTypes);
                    builder.And($"{tableAlias}.ResourceTypeId NOT IN ({excludedResourceTypeIds})");
                }

                if (options.ContinuationToken != null && options.IncludesContinuationToken == null)
                {
                    var sortOperator = options.SortDescending ? "<" : ">";

                    if (options.SortParameterName != null && options.SortParameterName.Equals(KnownQueryParameterNames.LastUpdated, StringComparison.OrdinalIgnoreCase))
                    {
                        builder.And($"{tableAlias}.ResourceSurrogateId {sortOperator} {options.ContinuationToken.ResourceSurrogateId}");
                    }
                    else
                    {
                        builder.And("(");
                        builder.IncreaseIndent(3);
                        builder.AppendLine($"({tableAlias}.ResourceSurrogateId {sortOperator} {options.ContinuationToken.ResourceSurrogateId} AND {tableAlias}.ResourceTypeId = {options.ContinuationToken.ResourceTypeId})");
                        builder.DecreaseIndent();
                        builder.Or($"{tableAlias}.ResourceTypeId {sortOperator} {options.ContinuationToken.ResourceTypeId}");
                        builder.DecreaseIndent(2);
                        builder.AppendLine(")");
                    }
                }
                else if (options.IncludesContinuationToken != null)
                {
                    builder.And($"{tableAlias}.ResourceSurrogateId >= {options.IncludesContinuationToken.MatchResourceSurrogateIdMin}")
                        .And($"{tableAlias}.ResourceSurrogateId <= {options.IncludesContinuationToken.MatchResourceSurrogateIdMax}")
                        .And($"{tableAlias}.ResourceTypeId = {options.IncludesContinuationToken.MatchResourceTypeId}");
                }
            }
        }

        public static void AddUnionCte(SqlQueryBuilder builder, string cteName, IList<string> targetCtes, bool includeSort = false, bool includeRow = true)
        {
            builder.BeginCte(cteName);

            // When sort CTE exists, the count CTE column order is:
            // ResourceTypeId, ResourceSurrogateId, SortValue, IsMatch, IsPartial, Row
            // Include CTEs have: ResourceTypeId, ResourceSurrogateId, IsMatch, IsPartial
            // We must use explicit columns to match the count CTE's column order
            var rowExpr = includeRow ? ", Row = 0" : string.Empty;
            var sortExpr = includeSort ? ", SortValue = NULL" : string.Empty;
            var columns = $"ResourceTypeId, ResourceSurrogateId{sortExpr}, IsMatch, IsPartial{rowExpr}";

            if (includeRow)
            {
                builder.AppendLine($"SELECT * FROM {targetCtes[0]}");
            }
            else
            {
                builder.AppendLine($"SELECT {columns} FROM {targetCtes[0]}");
            }

            foreach (var includeCteName in targetCtes.Skip(1))
            {
                builder.AppendLine("UNION ALL");
                builder.IncreaseIndent();
                builder.AppendLine($"SELECT {columns} FROM {includeCteName}");
                builder.Where($"NOT EXISTS (SELECT * FROM {targetCtes[0]} base WHERE base.ResourceSurrogateId = {includeCteName}.ResourceSurrogateId)");
                builder.DecreaseIndent();
            }

            builder.EndCte();
        }
    }
}
