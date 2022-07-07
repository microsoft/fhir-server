// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions
{
    /// <summary>
    /// <see cref="SearchParamTableExpression"/> extensions.
    /// </summary>
    internal static class SearchParamTableExpressionExtensions
    {
        /// <summary>
        /// Identifies if a <see cref="SearchParamTableExpression"/> contains a <see cref="UnionAllExpression"/>.
        /// </summary>
        /// <param name="expression">Instance of <see cref="SearchParamTableExpression"/> under evaluation.</param>
        public static bool HasUnionAllExpression(this SearchParamTableExpression expression)
        {
            IExpressionsContainer expressionContainer = expression.Predicate as IExpressionsContainer;
            return expressionContainer?.Expressions.Any(e => e is UnionAllExpression) ?? false;
        }

        /// <summary>
        /// Split the inner expressions from a <see cref="SearchParamTableExpression"/> into two groups: an existing <see cref="UnionAllExpression"/> and the other expressions.
        /// </summary>
        /// <param name="expression">Instance of <see cref="SearchParamTableExpression"/> under evaluation.</param>
        /// <param name="unionAllExpression">Instance of <see cref="UnionAllExpression"/>.</param>
        /// <param name="allOtherRemainingExpressions">Other exception different than <see cref="UnionAllExpression"/>.</param>
        /// <returns>Returns TRUE if the <see cref="SearchParamTableExpression"/> contains a <see cref="UnionAllExpression"/>.</returns>
        public static bool SplitExpressions(this SearchParamTableExpression expression, out UnionAllExpression unionAllExpression, out SearchParamTableExpression allOtherRemainingExpressions)
        {
            unionAllExpression = null;
            allOtherRemainingExpressions = null;

            IExpressionsContainer expressionContainer = expression.Predicate as IExpressionsContainer;
            UnionAllExpression tempUnionAllExpression = expressionContainer?.Expressions.SingleOrDefault(e => e is UnionAllExpression) as UnionAllExpression;

            if (tempUnionAllExpression != null)
            {
                IReadOnlyList<Expression> allOtherExpression = expressionContainer.Expressions.Where(e => e != tempUnionAllExpression).ToList();

                if (allOtherExpression.Any())
                {
                    allOtherRemainingExpressions = new SearchParamTableExpression(
                        expression.QueryGenerator,
                        new MultiaryExpression(MultiaryOperator.And, allOtherExpression),
                        SearchParamTableExpressionKind.Normal,
                        chainLevel: expression.ChainLevel + 1);
                }

                unionAllExpression = tempUnionAllExpression;

                return true;
            }

            return false;
        }

        /// <summary>
        /// Sort expression by query composition logic. <see cref="UnionAllExpression"/> always is the first expression to be processed, other expressions will follow it.
        /// </summary>
        /// <param name="expressions">Instance of <see cref="SearchParamTableExpression"/> under evaluation.</param>
        public static IReadOnlyList<SearchParamTableExpression> SortExpressionsByQueryLogic(this IReadOnlyList<SearchParamTableExpression> expressions)
        {
            List<SearchParamTableExpression> newTableExpressionSequence = new List<SearchParamTableExpression>(capacity: expressions.Count);

            foreach (SearchParamTableExpression tableExpression in expressions)
            {
                if (tableExpression.HasUnionAllExpression())
                {
                    newTableExpressionSequence.Insert(0, tableExpression);
                }
                else
                {
                    newTableExpressionSequence.Add(tableExpression);
                }
            }

            return newTableExpressionSequence;
        }
    }
}
