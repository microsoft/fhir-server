﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions
{
    internal class TableExpression : Expression
    {
        public TableExpression(
            NormalizedSearchParameterQueryGenerator searchParameterQueryGenerator,
            Expression normalizedPredicate,
            Expression denormalizedPredicate = null,
            TableExpressionKind kind = TableExpressionKind.Normal)
        {
            switch (normalizedPredicate)
            {
                case SearchParameterExpressionBase _:
                case CompartmentSearchExpression _:
                case ChainedExpression _:
                case null:
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(normalizedPredicate));
            }

            SearchParameterQueryGenerator = searchParameterQueryGenerator;
            NormalizedPredicate = normalizedPredicate;
            DenormalizedPredicate = denormalizedPredicate;
            Kind = kind;
        }

        public TableExpressionKind Kind { get; }

        public NormalizedSearchParameterQueryGenerator SearchParameterQueryGenerator { get; }

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
            return $"(Table {SearchParameterQueryGenerator.Table} Normalized:{NormalizedPredicate} Denormalized:{DenormalizedPredicate})";
        }
    }
}
