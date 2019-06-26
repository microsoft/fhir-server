// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;

namespace Microsoft.Health.Fhir.Core.Features.Search.Expressions
{
    /// <summary>
    /// An abstract <see cref="IExpressionVisitor{TContext,TOutput}"/> for rewriting an expression tree.
    /// Expression trees are treated as immutable, so if no changes have been made, the same instance is returned.
    /// </summary>
    /// <typeparam name="TContext">The type of the context parameter passed to each Visit method</typeparam>
    public abstract class ExpressionRewriter<TContext> : IExpressionVisitor<TContext, Expression>
    {
        public virtual Expression VisitSearchParameter(SearchParameterExpression expression, TContext context)
        {
            Expression visitedExpression = expression.Expression.AcceptVisitor(visitor: this, context: context);
            if (ReferenceEquals(visitedExpression, expression.Expression))
            {
                return expression;
            }

            return new SearchParameterExpression(expression.Parameter, visitedExpression);
        }

        public virtual Expression VisitBinary(BinaryExpression expression, TContext context)
        {
            return expression;
        }

        public virtual Expression VisitChained(ChainedExpression expression, TContext context)
        {
            Expression visitedExpression = expression.Expression.AcceptVisitor(visitor: this, context: context);
            if (ReferenceEquals(visitedExpression, expression.Expression))
            {
                return expression;
            }

            return new ChainedExpression(resourceType: expression.ResourceType, referenceSearchParameter: expression.ReferenceSearchParameter, targetResourceType: expression.TargetResourceType, reversed: expression.Reversed, expression: visitedExpression);
        }

        public virtual Expression VisitMissingField(MissingFieldExpression expression, TContext context)
        {
            return expression;
        }

        public virtual Expression VisitMissingSearchParameter(MissingSearchParameterExpression expression, TContext context)
        {
            return expression;
        }

        public virtual Expression VisitMultiary(MultiaryExpression expression, TContext context)
        {
            IReadOnlyList<Expression> rewrittenExpressions = VisitArray(expression.Expressions, context);
            return ReferenceEquals(rewrittenExpressions, expression.Expressions) ? expression : new MultiaryExpression(expression.MultiaryOperation, rewrittenExpressions);
        }

        public virtual Expression VisitString(StringExpression expression, TContext context)
        {
            return expression;
        }

        public virtual Expression VisitCompartment(CompartmentSearchExpression expression, TContext context)
        {
            return expression;
        }

        protected IReadOnlyList<TExpression> VisitArray<TExpression>(IReadOnlyList<TExpression> inputArray, TContext context)
            where TExpression : Expression
        {
            TExpression[] outputArray = null;

            for (var index = 0; index < inputArray.Count; index++)
            {
                var argument = inputArray[index];
                var rewrittenArgument = (TExpression)argument.AcceptVisitor(this, context);

                if (!ReferenceEquals(rewrittenArgument, argument))
                {
                    EnsureAllocatedAndPopulated(ref outputArray, inputArray, index);
                }

                if (outputArray != null)
                {
                    outputArray[index] = rewrittenArgument;
                }
            }

            return outputArray ?? inputArray;
        }

        protected static void EnsureAllocatedAndPopulated<TExpression>(ref TExpression[] destination, IReadOnlyList<TExpression> source, int count)
            where TExpression : Expression
        {
            if (destination == null)
            {
                destination = new TExpression[source.Count];
                for (int j = 0; j < count; j++)
                {
                    destination[j] = source[j];
                }
            }
        }

        protected static void EnsureAllocatedAndPopulated<TExpression>(ref List<TExpression> destination, IReadOnlyList<TExpression> source, int count)
            where TExpression : Expression
        {
            if (destination == null)
            {
                destination = new List<TExpression>();
                for (int j = 0; j < count; j++)
                {
                    destination.Add(source[j]);
                }
            }
        }
    }
}
