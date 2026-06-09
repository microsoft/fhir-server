// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.SqlServer;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators.Refactored.CteGenerators
{
    /// <summary>
    /// Generates CTEs for TOP expressions (pagination).
    /// </summary>
    internal class TopCteGenerator : ICteGenerator
    {
        public bool CanGenerate(SearchParamTableExpressionKind kind)
        {
            return kind == SearchParamTableExpressionKind.Top;
        }

        public void Generate(SearchParamTableExpression expression, QueryGenerationContext context)
        {
            var sb = context.StringBuilder;
            var tableExpressionName = QueryGenerationContext.GetCteTableName(context.CurrentCteIndex - 1);
            var sortHelper = new Helpers.SortingHelper();
            bool isSortValueNeeded = Helpers.SortingHelper.IsSortValueNeeded(context.SearchOptions);
            var sortExpression = isSortValueNeeded ? $"{tableExpressionName}.SortValue" : null;

            bool hasIncludeExpression = context.RootExpression.SearchParamTableExpressions.Any(t => t.Kind == SearchParamTableExpressionKind.Include);

            IndentedStringBuilder.IndentedScope indentedScope = default;
            if (hasIncludeExpression)
            {
                sb.Append("SELECT row_number() OVER (");
                AppendOrderBy(context, sortHelper);
                sb.AppendLine(") AS Row, *")
                    .AppendLine("FROM")
                    .AppendLine("(");

                indentedScope = sb.Indent();
            }

            const string selectStatement = "SELECT DISTINCT";
            sb.Append(selectStatement).Append(" TOP (")
                .Append(context.Parameters.AddParameter(context.SearchOptions.MaxItemCount + 1, includeInHash: false))
                .Append(") T1, Sid1, 1 AS IsMatch, 0 AS IsPartial ")
                .AppendLine(sortExpression == null ? string.Empty : $", {sortExpression}")
                .Append("FROM ").AppendLine(tableExpressionName);

            AppendOrderBy(context, sortHelper);
            sb.AppendLine();

            if (hasIncludeExpression)
            {
                indentedScope.Dispose();
                sb.AppendLine(") t");
            }

            context.MainSelectCte = context.GetCurrentCteTableName();
        }

        private static void AppendOrderBy(QueryGenerationContext context, Helpers.SortingHelper sortHelper)
        {
            var sb = context.StringBuilder;
            sb.Append("ORDER BY ");

            if (Helpers.SortingHelper.IsPrimaryKeySort(context.SearchOptions))
            {
                sb.AppendDelimited(", ", context.SearchOptions.Sort, (stringBuilder, sort) =>
                {
                    string column = sort.searchParameterInfo.Name switch
                    {
                        SearchParameterNames.ResourceType => "T1",
                        SearchParameterNames.LastUpdated => "Sid1",
                        _ => throw new System.InvalidOperationException($"Unexpected sort parameter {sort.searchParameterInfo.Name}"),
                    };
                    stringBuilder.Append(column).Append(" ").Append(sort.sortOrder == SortOrder.Ascending ? "ASC" : "DESC");
                });
            }
            else if (Helpers.SortingHelper.IsSortValueNeeded(context.SearchOptions))
            {
                sb.Append("SortValue ")
                    .Append(context.SearchOptions.Sort[0].sortOrder == SortOrder.Ascending ? "ASC" : "DESC")
                    .Append(", Sid1 ASC");
            }
            else
            {
                sb.Append("Sid1 ASC");
            }
        }
    }
}
