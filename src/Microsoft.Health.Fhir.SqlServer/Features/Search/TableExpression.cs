// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search
{
    internal class TableExpression : Expression
    {
        public TableExpression(Expression normalizedPredicate, Expression denormalizedPredicate = null)
        {
            EnsureArg.IsNotNull(normalizedPredicate, nameof(normalizedPredicate));
            switch (normalizedPredicate)
            {
                case SearchParameterExpressionBase _:
                case CompartmentSearchExpression _:
                case ChainedExpression _:
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(normalizedPredicate));
            }

            NormalizedPredicate = normalizedPredicate;
            DenormalizedPredicate = denormalizedPredicate;
        }

        public Expression NormalizedPredicate { get; }

        public Expression DenormalizedPredicate { get; }

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
            throw new System.NotImplementedException();
        }
    }
}
