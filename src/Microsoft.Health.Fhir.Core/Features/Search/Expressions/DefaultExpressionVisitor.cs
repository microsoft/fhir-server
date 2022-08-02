// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Features.Search.Expressions
{
    internal abstract class DefaultExpressionVisitor<TContext, TOutput> : IExpressionVisitor<TContext, TOutput>
    {
        private readonly Func<TOutput, TOutput, TOutput> _outputAggregator;

        protected DefaultExpressionVisitor()
            : this((acc, curr) => acc)
        {
        }

        protected DefaultExpressionVisitor(Func<TOutput, TOutput, TOutput> outputAggregator)
        {
            EnsureArg.IsNotNull(outputAggregator, nameof(outputAggregator));
            _outputAggregator = outputAggregator;
        }

        public virtual TOutput VisitMultiary(MultiaryExpression expression, TContext context)
        {
            return VisitExpressionsContainer(expression, context);
        }

        public virtual TOutput VisitUnion(UnionExpression expression, TContext context)
        {
            return VisitExpressionsContainer(expression, context);
        }

        public virtual TOutput VisitNotExpression(NotExpression expression, TContext context) => expression.Expression.AcceptVisitor(this, context);

        public virtual TOutput VisitSearchParameter(SearchParameterExpression expression, TContext context) => expression.Expression.AcceptVisitor(this, context);

        public virtual TOutput VisitBinary(BinaryExpression expression, TContext context) => default;

        public virtual TOutput VisitChained(ChainedExpression expression, TContext context) => expression.Expression.AcceptVisitor(this, context);

        public virtual TOutput VisitMissingField(MissingFieldExpression expression, TContext context) => default;

        public virtual TOutput VisitMissingSearchParameter(MissingSearchParameterExpression expression, TContext context) => default;

        public virtual TOutput VisitString(StringExpression expression, TContext context) => default;

        public virtual TOutput VisitCompartment(CompartmentSearchExpression expression, TContext context) => default;

        public virtual TOutput VisitInclude(IncludeExpression expression, TContext context) => default;

        public virtual TOutput VisitSortParameter(SortExpression expression, TContext context) => default;

        public virtual TOutput VisitIn<T>(InExpression<T> expression, TContext context) => default;

        private TOutput VisitExpressionsContainer(IExpressionsContainer expression, TContext context)
        {
            TOutput result = default;
            for (var i = 0; i < expression.Expressions.Count; i++)
            {
                var operand = expression.Expressions[i];
                TOutput currentResult = operand.AcceptVisitor(this, context);
                if (i == 0)
                {
                    result = currentResult;
                }
                else
                {
                    result = _outputAggregator(result, currentResult);
                }
            }

            return result;
        }
    }
}
