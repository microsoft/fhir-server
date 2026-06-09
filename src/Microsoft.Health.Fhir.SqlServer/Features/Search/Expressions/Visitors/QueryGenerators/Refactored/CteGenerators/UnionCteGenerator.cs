// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.SqlServer;
using Microsoft.Health.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators.Refactored.CteGenerators
{
    /// <summary>
    /// Generates CTEs for UNION operations (e.g., compartment searches).
    /// Handles combining results from multiple resource types or scopes.
    /// </summary>
    internal class UnionCteGenerator : ICteGenerator
    {
        private readonly IHistoryClauseBuilder _historyClauseBuilder;
        private readonly IDeletedClauseBuilder _deletedClauseBuilder;

        public UnionCteGenerator(
            IHistoryClauseBuilder historyClauseBuilder,
            IDeletedClauseBuilder deletedClauseBuilder)
        {
            _historyClauseBuilder = historyClauseBuilder;
            _deletedClauseBuilder = deletedClauseBuilder;
        }

        public bool CanGenerate(SearchParamTableExpressionKind kind)
        {
            return kind == SearchParamTableExpressionKind.Union;
        }

        public void Generate(SearchParamTableExpression expression, QueryGenerationContext context)
        {
            var sb = context.StringBuilder;
            var cteName = context.GetNextCteTableName();
            var tableName = DetermineTableName(expression, context);

            sb.Append(cteName).AppendLine(" AS").AppendLine("(");

            using (sb.Indent())
            {
                sb.Append("SELECT ")
                    .Append(VLatest.Resource.ResourceTypeId, null).Append(" AS T1, ")
                    .Append(VLatest.Resource.ResourceSurrogateId, null).AppendLine(" AS Sid1");

                sb.Append("FROM ").AppendLine(tableName);

                using (var delimited = sb.BeginDelimitedWhereClause())
                {
                    AppendFilterClauses(expression, context, delimited, tableName);
                }
            }

            sb.AppendLine("),");
        }

        private static Table DetermineTableName(SearchParamTableExpression expression, QueryGenerationContext context)
        {
            var tableName = expression.QueryGenerator.Table;

            if (expression.Predicate is SearchParameterExpression spe &&
                spe.Parameter.ColumnLocation().HasFlag(SearchParameterColumnLocation.ResourceTable))
            {
                return VLatest.Resource;
            }

            if (expression.Predicate is MultiaryExpression multiary)
            {
                bool allAreResourceTypeOrId = multiary.Expressions.All(e =>
                    e is SearchParameterExpression searchParamExpr &&
                    (searchParamExpr.Parameter.Name == SearchParameterNames.ResourceType ||
                     searchParamExpr.Parameter.Name == SearchParameterNames.Id));

                if (allAreResourceTypeOrId)
                {
                    return VLatest.Resource;
                }
            }

            return tableName;
        }

        private void AppendFilterClauses(
            SearchParamTableExpression expression,
            QueryGenerationContext context,
            IndentedStringBuilder.DelimitedScope delimited,
            Table tableName)
        {
            _historyClauseBuilder.AppendHistoryClause(
                delimited,
                context.SearchOptions.ResourceVersionTypes,
                context,
                expression,
                null,
                tableName);

            if (tableName.Equals(VLatest.Resource))
            {
                _deletedClauseBuilder.AppendDeletedClause(
                    delimited,
                    context.SearchOptions.ResourceVersionTypes,
                    context);
            }

            if (expression.Predicate != null && expression.Predicate is not CompartmentSearchExpression)
            {
                delimited.BeginDelimitedElement();
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
}
