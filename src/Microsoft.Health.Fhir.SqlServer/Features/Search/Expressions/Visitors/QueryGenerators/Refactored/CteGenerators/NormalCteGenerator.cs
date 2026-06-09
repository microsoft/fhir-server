// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.SqlServer;
using Microsoft.Health.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators.Refactored.CteGenerators
{
    /// <summary>
    /// Generates CTEs for normal search parameter expressions.
    /// Handles the most common case of search parameter filtering.
    /// </summary>
    internal class NormalCteGenerator : ICteGenerator
    {
        private readonly IHistoryClauseBuilder _historyClauseBuilder;

        public NormalCteGenerator(IHistoryClauseBuilder historyClauseBuilder)
        {
            _historyClauseBuilder = historyClauseBuilder;
        }

        public bool CanGenerate(SearchParamTableExpressionKind kind)
        {
            return kind == SearchParamTableExpressionKind.Normal;
        }

        public void Generate(SearchParamTableExpression expression, QueryGenerationContext context)
        {
            var sb = context.StringBuilder;
            Table specialCaseTableName = expression.QueryGenerator.Table;

            if (expression.ChainLevel == 0)
            {
                GenerateChainLevelZero(expression, context, sb);
            }
            else if (expression.ChainLevel == 1 && context.UnionVisited)
            {
                specialCaseTableName = GenerateChainLevelOneAfterUnion(expression, context, sb);
            }
            else
            {
                GenerateHigherChainLevel(expression, context, sb);
            }

            AppendJoinWithPredecessorIfNeeded(expression, context);
            AppendWhereClause(expression, context, specialCaseTableName);
        }

        private static void GenerateChainLevelZero(
            SearchParamTableExpression expression,
            QueryGenerationContext context,
            IndentedStringBuilder sb)
        {
            int predecessorIndex = Helpers.CteJoinHelper.FindRestrictingPredecessorIndex(context);
            bool isInSortMode = context.SortVisited && context.SearchOptions.Sort?.Count > 0;

            if (!isInSortMode || predecessorIndex < 0)
            {
                sb.Append("SELECT ")
                    .Append(VLatest.Resource.ResourceTypeId, null).Append(" AS T1, ")
                    .Append(VLatest.Resource.ResourceSurrogateId, null).AppendLine(" AS Sid1")
                    .Append("FROM ").AppendLine(expression.QueryGenerator.Table);
            }
            else
            {
                // Sort mode: join with previous CTE to propagate SortValue
                var cte = QueryGenerationContext.GetCteTableName(predecessorIndex);
                sb.Append("SELECT ")
                    .Append(VLatest.Resource.ResourceTypeId, null).Append(" AS T1, ")
                    .Append(VLatest.Resource.ResourceSurrogateId, null).Append(" AS Sid1, ")
                    .Append(cte).AppendLine(".SortValue")
                    .Append("FROM ").AppendLine(expression.QueryGenerator.Table)
                    .Append("     JOIN ").Append(cte)
                    .Append(" ON ").Append(VLatest.Resource.ResourceTypeId, null).Append(" = ").Append(cte).Append(".T1")
                    .Append(" AND ").Append(VLatest.Resource.ResourceSurrogateId, null).Append(" = ").Append(cte).AppendLine(".Sid1");
            }
        }

        private static Table GenerateChainLevelOneAfterUnion(
            SearchParamTableExpression expression,
            QueryGenerationContext context,
            IndentedStringBuilder sb)
        {
            Table specialCaseTableName = expression.QueryGenerator.Table;
            var searchParameterExpressionPredicate = CheckExpressionOrFirstChildIsSearchParam(expression.Predicate);
            if (searchParameterExpressionPredicate != null &&
                searchParameterExpressionPredicate.Parameter.ColumnLocation().HasFlag(SearchParameterColumnLocation.ResourceTable))
            {
                specialCaseTableName = VLatest.Resource;
            }

            int predecessorIndex = Helpers.CteJoinHelper.FindRestrictingPredecessorIndex(context);
            sb.Append("SELECT T1, Sid1, ")
                .Append(VLatest.Resource.ResourceTypeId, null).Append(" AS T2, ")
                .Append(VLatest.Resource.ResourceSurrogateId, null).AppendLine(" AS Sid2")
                .Append("FROM ").AppendLine(specialCaseTableName)
                .Append("     JOIN ").Append(QueryGenerationContext.GetCteTableName(predecessorIndex))
                .Append(" ON ").Append(VLatest.Resource.ResourceTypeId, null).Append(" = ").Append(context.FirstChainAfterUnionVisited ? "T2" : "T1")
                .Append(" AND ").Append(VLatest.Resource.ResourceSurrogateId, null).Append(" = ").AppendLine(context.FirstChainAfterUnionVisited ? "Sid2" : "Sid1");

            context.FirstChainAfterUnionVisited = true;
            return specialCaseTableName;
        }

        private static void GenerateHigherChainLevel(
            SearchParamTableExpression expression,
            QueryGenerationContext context,
            IndentedStringBuilder sb)
        {
            int predecessorIndex = Helpers.CteJoinHelper.FindRestrictingPredecessorIndex(context);
            sb.Append("SELECT T1, Sid1, ")
                .Append(VLatest.Resource.ResourceTypeId, null).Append(" AS T2, ")
                .Append(VLatest.Resource.ResourceSurrogateId, null).AppendLine(" AS Sid2")
                .Append("FROM ").AppendLine(expression.QueryGenerator.Table)
                .Append("     JOIN ").Append(QueryGenerationContext.GetCteTableName(predecessorIndex))
                .Append(" ON ").Append(VLatest.Resource.ResourceTypeId, null).Append(" = T2")
                .Append(" AND ").Append(VLatest.Resource.ResourceSurrogateId, null).Append(" = ").AppendLine("Sid2");
        }

        private static void AppendJoinWithPredecessorIfNeeded(
            SearchParamTableExpression expression,
            QueryGenerationContext context)
        {
            bool shouldUseJoin = Helpers.CteJoinHelper.ShouldUseInnerJoin(context);
            bool isInSortMode = context.SortVisited && context.SearchOptions.Sort?.Count > 0;

            if (shouldUseJoin &&
                expression.ChainLevel == 0 &&
                !isInSortMode &&
                !context.SearchOptions.SkipAppendIntersectionWithPredecessor)
            {
                Helpers.CteJoinHelper.AppendIntersectionWithPredecessorUsingInnerJoin(context, expression);
            }
        }

        private void AppendWhereClause(
            SearchParamTableExpression expression,
            QueryGenerationContext context,
            Table tableName)
        {
            using (var delimited = context.StringBuilder.BeginDelimitedWhereClause())
            {
                _historyClauseBuilder.AppendHistoryClause(
                    delimited,
                    context.SearchOptions.ResourceVersionTypes,
                    context,
                    expression,
                    null,
                    tableName);

                bool isInSortMode = context.SortVisited && context.SearchOptions.Sort?.Count > 0;
                if (expression.ChainLevel == 0 && !isInSortMode && !Helpers.CteJoinHelper.ShouldUseInnerJoin(context))
                {
                    if (!context.SearchOptions.SkipAppendIntersectionWithPredecessor)
                    {
                        Helpers.CteJoinHelper.AppendIntersectionWithPredecessor(delimited, context, expression);
                    }
                }

                if (expression.Predicate != null)
                {
                    delimited.BeginDelimitedElement();
                    CheckForIdentifierSearchParams(expression.Predicate, context);

                    var generatorContext = new SearchParameterQueryGeneratorContext(
                        context.StringBuilder,
                        context.Parameters,
                        context.Model,
                        context.SchemaInfo,
                        context.IsAsyncOperation);
                    expression.Predicate.AcceptVisitor(expression.QueryGenerator, generatorContext);
                }
            }
        }

        private static SearchParameterExpression CheckExpressionOrFirstChildIsSearchParam(Expression expression)
        {
            if (expression is SearchParameterExpression searchParam)
            {
                return searchParam;
            }

            if (expression is MultiaryExpression multiary && multiary.Expressions.Count > 0)
            {
                return multiary.Expressions[0] as SearchParameterExpression;
            }

            return null;
        }

        private static void CheckForIdentifierSearchParams(Expression expression, QueryGenerationContext context)
        {
            // Simplified version - check for identifier parameter name in expression
            // Full implementation would use ExpressionContainsParameterVisitor
            context.SearchParamCount++;
        }
    }
}
