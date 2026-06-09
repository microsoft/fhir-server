// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators.Refactored.CteGenerators
{
    /// <summary>
    /// Generates CTEs for sort operations with search parameter values.
    /// </summary>
    internal class SortCteGenerator : ICteGenerator
    {
        private readonly IHistoryClauseBuilder _historyClauseBuilder;

        public SortCteGenerator(IHistoryClauseBuilder historyClauseBuilder)
        {
            _historyClauseBuilder = historyClauseBuilder;
        }

        public bool CanGenerate(SearchParamTableExpressionKind kind)
        {
            return kind == SearchParamTableExpressionKind.Sort || kind == SearchParamTableExpressionKind.SortWithFilter;
        }

        public void Generate(SearchParamTableExpression expression, QueryGenerationContext context)
        {
            // Sort CTE generation requires implementation
            throw new System.NotImplementedException("Sort CTE generation requires full implementation from original code");
        }
    }
}
