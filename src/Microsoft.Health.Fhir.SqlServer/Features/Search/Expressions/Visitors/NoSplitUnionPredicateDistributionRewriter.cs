// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors
{
    /// <summary>
    /// Distributes the remaining predicate that has been ANDed onto a no-split union table expression
    /// (a <see cref="UnionExpression"/> flagged with <see cref="UnionExpression.DoNotSplitIntoSeparateCtes"/>)
    /// into each branch of the union, and removes the now-redundant outer AND.
    /// <para>
    /// Earlier rewriters (notably <see cref="ResourceColumnPredicatePushdownRewriter"/>) push resource-column
    /// predicates such as <c>ResourceTypeId</c> onto the union table expression as <c>And(union, remainder)</c>.
    /// Leaving that shape causes the SQL generator to emit the union in one CTE and the remainder in a separate,
    /// downstream CTE. By rewriting <c>And(Union(b1, b2), R)</c> into <c>Union(And(b1, R), And(b2, R))</c> the
    /// remainder is folded directly into each inline <c>UNION ALL</c> branch, keeping the no-split union as a
    /// single self-contained CTE. The transform is semantically equivalent because AND distributes over a union.
    /// </para>
    /// </summary>
    internal class NoSplitUnionPredicateDistributionRewriter : SqlExpressionRewriterWithInitialContext<object>
    {
        public static readonly NoSplitUnionPredicateDistributionRewriter Instance = new NoSplitUnionPredicateDistributionRewriter();

        public override Expression VisitSqlRoot(SqlRootExpression expression, object context)
        {
            List<SearchParamTableExpression> newTableExpressions = null;

            for (int i = 0; i < expression.SearchParamTableExpressions.Count; i++)
            {
                SearchParamTableExpression tableExpression = expression.SearchParamTableExpressions[i];

                if (TryDistribute(tableExpression.Predicate, out UnionExpression distributedUnion))
                {
                    newTableExpressions ??= new List<SearchParamTableExpression>(expression.SearchParamTableExpressions);
                    newTableExpressions[i] = new SearchParamTableExpression(tableExpression.QueryGenerator, distributedUnion, tableExpression.Kind, tableExpression.ChainLevel);
                }
            }

            if (newTableExpressions == null)
            {
                return expression;
            }

            return new SqlRootExpression(newTableExpressions, expression.ResourceTableExpressions);
        }

        private static bool TryDistribute(Expression predicate, out UnionExpression distributedUnion)
        {
            distributedUnion = null;

            if (predicate is not MultiaryExpression multiary || multiary.MultiaryOperation != MultiaryOperator.And)
            {
                return false;
            }

            UnionExpression union = null;
            var remainder = new List<Expression>(multiary.Expressions.Count);

            foreach (Expression child in multiary.Expressions)
            {
                if (union == null && child is UnionExpression { DoNotSplitIntoSeparateCtes: true } candidate)
                {
                    union = candidate;
                }
                else
                {
                    remainder.Add(child);
                }
            }

            // Only distribute a single no-split union when there is something to fold in, and never when the
            // remainder itself contains a union (which would create an invalid nested-union branch).
            if (union == null || remainder.Count == 0 || remainder.Any(r => r is UnionExpression))
            {
                return false;
            }

            var newBranches = new List<Expression>(union.Expressions.Count);

            foreach (Expression branch in union.Expressions)
            {
                var branchExpressions = new List<Expression>(remainder.Count + 1) { branch };
                branchExpressions.AddRange(remainder);
                newBranches.Add(Expression.And(branchExpressions));
            }

            distributedUnion = new UnionExpression(union.Operator, newBranches) { DoNotSplitIntoSeparateCtes = true };
            return true;
        }
    }
}
