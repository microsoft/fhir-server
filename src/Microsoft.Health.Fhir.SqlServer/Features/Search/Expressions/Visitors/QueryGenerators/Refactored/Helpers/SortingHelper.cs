// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Data;
using System.Linq;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.SqlServer.Features.Schema.Model;
using SortOrder = Microsoft.Health.Fhir.Core.Features.Search.SortOrder;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators.Refactored.Helpers
{
    /// <summary>
    /// Helper for building ORDER BY clauses and managing sort-related operations.
    /// </summary>
    internal class SortingHelper
    {
        public static bool IsSortValueNeeded(SearchOptions searchOptions)
        {
            return searchOptions?.Sort?.Count > 0 && !IsPrimaryKeySort(searchOptions);
        }

        public static bool IsPrimaryKeySort(SearchOptions searchOptions)
        {
            if (searchOptions?.Sort == null || searchOptions.Sort.Count == 0)
            {
                return false;
            }

            return searchOptions.Sort.All(s =>
                s.searchParameterInfo.Name == SearchParameterNames.ResourceType ||
                s.searchParameterInfo.Name == SearchParameterNames.LastUpdated);
        }

        public static (Column SortColumnName, SqlDbType ColumnType, bool RequiresLength, long MaxLength) GetSortDetails(SearchOptions searchOptions)
        {
            if (searchOptions?.Sort == null || searchOptions.Sort.Count == 0)
            {
                throw new InvalidOperationException("Cannot get sort details without sort parameters");
            }

            var sortParam = searchOptions.Sort[0];

            // For now, use a default column - this should be properly implemented based on search parameter type
            var column = VLatest.Resource.ResourceSurrogateId;
            var dbType = column.Metadata.SqlDbType;
            bool requiresLength = dbType != SqlDbType.DateTime2 && dbType != SqlDbType.DateTime;
            long maxLength = column.Metadata.MaxLength;

            return (column, dbType, requiresLength, maxLength);
        }

        public static void AppendOrderByClause(QueryGenerationContext context, string orderTableAlias)
        {
            var sb = context.StringBuilder;
            var searchOptions = context.SearchOptions;
            bool hasIncludes = context.RootExpression.SearchParamTableExpressions.Any(t => t.Kind == SearchParamTableExpressionKind.Include);

            if (hasIncludes)
            {
                sb.Append("IsMatch DESC, ");
            }

            if (IsPrimaryKeySort(searchOptions))
            {
                AppendPrimaryKeySort(context, orderTableAlias, hasIncludes);
            }
            else if (IsSortValueNeeded(searchOptions) && !searchOptions.IsIncludesOperation)
            {
                AppendSortValueSort(context, orderTableAlias, hasIncludes);
            }
            else
            {
                AppendDefaultSort(context, orderTableAlias);
            }
        }

        private static void AppendPrimaryKeySort(QueryGenerationContext context, string orderTableAlias, bool hasIncludes)
        {
            var sb = context.StringBuilder;
            var searchOptions = context.SearchOptions;

            sb.AppendDelimited(", ", searchOptions.Sort, (stringBuilder, sort) =>
            {
                Column column = sort.searchParameterInfo.Name switch
                {
                    SearchParameterNames.ResourceType => VLatest.Resource.ResourceTypeId,
                    SearchParameterNames.LastUpdated => VLatest.Resource.ResourceSurrogateId,
                    _ => throw new InvalidOperationException($"Unexpected sort parameter {sort.searchParameterInfo.Name}"),
                };

                if (hasIncludes)
                {
                    stringBuilder.Append("(CASE WHEN IsMatch = 1 THEN ");
                    stringBuilder.Append(column, orderTableAlias);
                    stringBuilder.Append(" ELSE NULL END) ");
                }
                else
                {
                    stringBuilder.Append(column, orderTableAlias).Append(" ");
                }

                stringBuilder.Append(sort.sortOrder == SortOrder.Ascending ? "ASC" : "DESC");
            });

            if (hasIncludes)
            {
                sb.Append(", (CASE WHEN IsMatch = 0 THEN ").Append(VLatest.Resource.ResourceTypeId, orderTableAlias).Append(" ELSE NULL END) ASC, ");
                sb.Append("(CASE WHEN IsMatch = 0 THEN ").Append(VLatest.Resource.ResourceSurrogateId, orderTableAlias).Append(" ELSE NULL END) ASC ");
            }
        }

        private static void AppendSortValueSort(QueryGenerationContext context, string orderTableAlias, bool hasIncludes)
        {
            var sb = context.StringBuilder;
            var searchOptions = context.SearchOptions;

            if (hasIncludes)
            {
                sb.Append("(CASE WHEN IsMatch = 1 THEN ")
                    .Append(orderTableAlias)
                    .Append(".SortValue ELSE NULL END) ");
            }
            else
            {
                sb.Append(orderTableAlias).Append(".SortValue ");
            }

            sb.Append(searchOptions.Sort[0].sortOrder == SortOrder.Ascending ? "ASC" : "DESC").Append(", ")
                .Append(VLatest.Resource.ResourceTypeId, orderTableAlias).Append(" ASC, ")
                .Append(VLatest.Resource.ResourceSurrogateId, orderTableAlias).Append(" ASC ");
        }

        private static void AppendDefaultSort(QueryGenerationContext context, string orderTableAlias)
        {
            var sb = context.StringBuilder;
            sb.Append(VLatest.Resource.ResourceTypeId, orderTableAlias).Append(" ASC, ")
                .Append(VLatest.Resource.ResourceSurrogateId, orderTableAlias).Append(" ASC ");
        }
    }
}
