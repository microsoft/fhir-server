// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions
{
    /// <summary>
    /// An expression over a search param or compartment table.
    /// </summary>
    internal class TableExpression : Expression
    {
        /// <summary>
        /// Creates a new instance of the <see cref="TableExpression"/> class.
        /// </summary>
        /// <param name="searchParameterQueryGenerator">The search parameter query generator</param>
        /// <param name="normalizedPredicate">The search expression over a columns belonging exclusively to a search parameter table.
        /// Applies to the chain target if a chained expression.</param>
        /// <param name="denormalizedPredicate">The search expression over columns that exist the Resource table. Applies to the chain target if a chained expression.</param>
        /// <param name="kind">The table expression kind.</param>
        /// <param name="chainLevel">The nesting chain nesting level of the current expression. 0 if not a chain expression.</param>
        /// <param name="denormalizedPredicateOnChainRoot">The search expression over columns that exist the Resource table. Applies to the chain root if a chained expression.</param>
        public TableExpression(
            NormalizedSearchParameterQueryGenerator searchParameterQueryGenerator,
            Expression normalizedPredicate,
            Expression denormalizedPredicate,
            TableExpressionKind kind,
            int chainLevel = 0,
            Expression denormalizedPredicateOnChainRoot = null)
        {
            switch (normalizedPredicate)
            {
                case SearchParameterExpressionBase _:
                case CompartmentSearchExpression _:
                case ChainedExpression _:
                case IncludeExpression _:
                case null:
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(normalizedPredicate));
            }

            SearchParameterQueryGenerator = searchParameterQueryGenerator;
            NormalizedPredicate = normalizedPredicate;
            DenormalizedPredicate = denormalizedPredicate;
            Kind = kind;
            ChainLevel = chainLevel;
            DenormalizedPredicateOnChainRoot = denormalizedPredicateOnChainRoot;
        }

        public TableExpressionKind Kind { get; }

        /// <summary>
        /// The nesting chain nesting level of the current expression. 0 if not a chain expression.
        /// </summary>
        public int ChainLevel { get; }

        public NormalizedSearchParameterQueryGenerator SearchParameterQueryGenerator { get; }

        /// <summary>
        /// The search expression over a columns belonging exclusively to a search parameter table.
        /// Applies to the chain target if a chained expression.
        /// </summary>
        public Expression NormalizedPredicate { get; }

        /// <summary>
        /// The search expression over columns that exist the Resource table. Applies to the chain target if a chained expression.
        /// </summary>
        public Expression DenormalizedPredicate { get; }

        /// <summary>
        /// The search expression over columns that exist the Resource table. Applies to the chain root if a chained expression.
        /// </summary>
        public Expression DenormalizedPredicateOnChainRoot { get; }

        public override TOutput AcceptVisitor<TContext, TOutput>(IExpressionVisitor<TContext, TOutput> visitor, TContext context)
        {
            return AcceptVisitor((ISqlExpressionVisitor<TContext, TOutput>)visitor, context);
        }

        public TOutput AcceptVisitor<TContext, TOutput>(ISqlExpressionVisitor<TContext, TOutput> visitor, TContext context)
        {
            return visitor.VisitTable(this, context);
        }

        public override string ToString()
        {
            return $"(Table {Kind} {(ChainLevel == 0 ? null : $"ChainLevel:{ChainLevel} ")}{SearchParameterQueryGenerator?.Table} Normalized:{NormalizedPredicate} Denormalized:{DenormalizedPredicate} DenormalizedOnChainRoot:{DenormalizedPredicateOnChainRoot})";
        }
    }
}
