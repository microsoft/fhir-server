// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.SqlServer;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators.Refactored.CteGenerators
{
    /// <summary>
    /// Generates CTEs for NOT EXISTS (missing parameter) expressions.
    /// </summary>
    internal class NotExistsCteGenerator : ICteGenerator
    {
        private readonly IHistoryClauseBuilder _historyClauseBuilder;

        public NotExistsCteGenerator(IHistoryClauseBuilder historyClauseBuilder)
        {
            _historyClauseBuilder = historyClauseBuilder;
        }

        public bool CanGenerate(SearchParamTableExpressionKind kind)
        {
            return kind == SearchParamTableExpressionKind.NotExists;
        }

        public void Generate(SearchParamTableExpression expression, QueryGenerationContext context)
        {
            var sb = context.StringBuilder;
            bool isInSortMode = context.SortVisited && context.SearchOptions.Sort?.Count > 0;

            sb.Append("SELECT T1, Sid1");
            sb.AppendLine(isInSortMode ? ", SortValue" : string.Empty);
            sb.Append("FROM ").AppendLine(QueryGenerationContext.GetCteTableName(context.CurrentCteIndex - 1));
            sb.AppendLine("WHERE Sid1 NOT IN").AppendLine("(");

            using (sb.Indent())
            {
                sb.Append("SELECT ").AppendLine(VLatest.Resource.ResourceSurrogateId, null)
                    .Append("FROM ").AppendLine(expression.QueryGenerator.Table);

                using (var delimited = sb.BeginDelimitedWhereClause())
                {
                    _historyClauseBuilder.AppendHistoryClause(
                        delimited,
                        context.SearchOptions.ResourceVersionTypes,
                        context,
                        expression);

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

            sb.AppendLine(")");
        }
    }
}
