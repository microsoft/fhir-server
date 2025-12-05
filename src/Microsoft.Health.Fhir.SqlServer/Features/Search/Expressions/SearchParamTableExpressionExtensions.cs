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
        /// Identifies if a <see cref="SearchParamTableExpression"/> contains a <see cref="UnionExpression"/>.
        /// </summary>
        /// <param name="expression">Instance of <see cref="SearchParamTableExpression"/> under evaluation.</param>
        public static bool HasUnionAllExpression(this SearchParamTableExpression expression)
        {
            IExpressionsContainer expressionContainer = expression.Predicate as IExpressionsContainer;
            return expressionContainer?.Expressions.Any(e => e is UnionExpression) ?? false;
        }

        /// <summary>
        /// Split the inner expressions from a <see cref="SearchParamTableExpression"/> into two groups: an existing <see cref="UnionExpression"/> and the other expressions.
        /// </summary>
        /// <param name="expression">Instance of <see cref="SearchParamTableExpression"/> under evaluation.</param>
        /// <param name="unionExpression">Instance of <see cref="UnionExpression"/>.</param>
        /// <param name="allOtherRemainingExpressions">Other exception different than <see cref="UnionExpression"/>.</param>
        /// <returns>Returns TRUE if the <see cref="SearchParamTableExpression"/> contains a <see cref="UnionExpression"/>.</returns>
        public static bool SplitExpressions(this SearchParamTableExpression expression, out UnionExpression unionExpression, out SearchParamTableExpression allOtherRemainingExpressions)
        {
            unionExpression = null;
            allOtherRemainingExpressions = null;

            IExpressionsContainer expressionContainer = expression.Predicate as IExpressionsContainer;

            if (expressionContainer != null)
            {
                UnionExpression tempUnionAllExpression = expressionContainer.Expressions.SingleOrDefault(e => e is UnionExpression) as UnionExpression;

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

                    unionExpression = tempUnionAllExpression;

                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Identifies if a <see cref="SearchParamTableExpression"/> contains a <see cref="UnionExpression"/> with SmartV2 flag.
        /// </summary>
        /// <param name="expression">Instance of <see cref="SearchParamTableExpression"/> under evaluation.</param>
        public static bool HasSmartV2UnionExpression(this SearchParamTableExpression expression)
        {
            return ContainsSmartV2UnionFlag(expression.Predicate);
        }

        /// <summary>
        /// Sort expression by query composition logic. <see cref="UnionExpression"/> always is the first expression to be processed
        /// with SmartV2 union expressions appearing at the end of all union expressions, followed by other expressions.
        /// </summary>
        /// <param name="expressions">Instance of <see cref="SearchParamTableExpression"/> under evaluation.</param>
        public static IReadOnlyList<SearchParamTableExpression> SortExpressionsByQueryLogic(this IReadOnlyList<SearchParamTableExpression> expressions)
        {
            var regularUnions = new List<SearchParamTableExpression>();
            var smartV2Unions = new List<SearchParamTableExpression>();
            var nonUnions = new List<SearchParamTableExpression>();

            foreach (SearchParamTableExpression tableExpression in expressions)
            {
                if (tableExpression.HasUnionAllExpression())
                {
                    if (tableExpression.HasSmartV2UnionExpression())
                    {
                        smartV2Unions.Add(tableExpression);
                    }
                    else
                    {
                        regularUnions.Add(tableExpression);
                    }
                }
                else
                {
                    nonUnions.Add(tableExpression);
                }
            }

            // Combine in the desired order: regular unions first, then SmartV2 unions, then non-unions
            var result = new List<SearchParamTableExpression>(capacity: expressions.Count);
            result.AddRange(regularUnions);
            result.AddRange(smartV2Unions);
            result.AddRange(nonUnions);

            return result;
        }

        /// <summary>
        /// Get Count of all Union All Expressions.
        /// </summary>
        /// <param name="expressions">Instance of <see cref="SearchParamTableExpression"/> under evaluation.</param>
        public static int GetCountOfUnionAllExpressions(this IReadOnlyList<SearchParamTableExpression> expressions)
        {
            return expressions.Count(tableExpression => tableExpression.HasUnionAllExpression());
        }

        /// <summary>
        /// Recursively checks whether the given expression or any of its descendant expressions
        /// has the <see cref="Expression.IsSmartV2UnionExpressionForScopesSearchParameters"/> flag set to true.
        /// </summary>
        /// <param name="expression">The root expression to search.</param>
        /// <returns>True if any expression in the tree has the flag; otherwise, false.</returns>
        private static bool ContainsSmartV2UnionFlag(Expression expression)
        {
            if (expression == null)
            {
                return false;
            }

            // If this expression has the flag, return true.
            if (expression.IsSmartV2UnionExpressionForScopesSearchParameters)
            {
                return true;
            }

            // Check if expression can contain child expressions.
            if (expression is IExpressionsContainer container)
            {
                return container.Expressions.Any(ContainsSmartV2UnionFlag);
            }

            return false;
        }
    }
}
