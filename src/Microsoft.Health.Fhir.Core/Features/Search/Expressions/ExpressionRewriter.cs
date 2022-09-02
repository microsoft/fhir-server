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

            /*SortRewriter will return null expression if it finds _sort param same as on of filter params.
             But in case of chained names we do need to keep it. #94770*/
            if (visitedExpression == null)
            {
                return new ChainedExpression(resourceTypes: expression.ResourceTypes, referenceSearchParameter: expression.ReferenceSearchParameter, targetResourceTypes: expression.TargetResourceTypes, reversed: expression.Reversed, expression: expression);
            }
            else
            {
                return new ChainedExpression(resourceTypes: expression.ResourceTypes, referenceSearchParameter: expression.ReferenceSearchParameter, targetResourceTypes: expression.TargetResourceTypes, reversed: expression.Reversed, expression: visitedExpression);
            }
        }

        public virtual Expression VisitMissingField(MissingFieldExpression expression, TContext context)
        {
            return expression;
        }

        public virtual Expression VisitMissingSearchParameter(MissingSearchParameterExpression expression, TContext context)
        {
            return expression;
        }

        public virtual Expression VisitNotExpression(NotExpression expression, TContext context)
        {
            Expression visitedExpression = expression.Expression.AcceptVisitor(visitor: this, context: context);
            if (ReferenceEquals(visitedExpression, expression.Expression))
            {
                return expression;
            }

            return new NotExpression(visitedExpression);
        }

        public virtual Expression VisitMultiary(MultiaryExpression expression, TContext context)
        {
            IReadOnlyList<Expression> rewrittenExpressions = VisitArray(expression.Expressions, context);
            return ReferenceEquals(rewrittenExpressions, expression.Expressions) ? expression : new MultiaryExpression(expression.MultiaryOperation, rewrittenExpressions);
        }

        public virtual Expression VisitUnion(UnionExpression expression, TContext context)
        {
            IReadOnlyList<Expression> rewrittenExpressions = VisitArray(expression.Expressions, context);
            return ReferenceEquals(rewrittenExpressions, expression.Expressions) ? expression : new UnionExpression(expression.Operator, rewrittenExpressions);
        }

        public virtual Expression VisitString(StringExpression expression, TContext context)
        {
            return expression;
        }

        public virtual Expression VisitCompartment(CompartmentSearchExpression expression, TContext context)
        {
            return expression;
        }

        public virtual Expression VisitInclude(IncludeExpression expression, TContext context)
        {
            return expression;
        }

        public virtual Expression VisitSortParameter(SortExpression expression, TContext context)
        {
            return expression;
        }

        public Expression VisitIn<T>(InExpression<T> expression, TContext context)
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

#pragma warning disable CA1002 // Do not expose generic lists
        protected static void EnsureAllocatedAndPopulated<TExpression>(ref List<TExpression> destination, IReadOnlyList<TExpression> source, int count)
#pragma warning restore CA1002 // Do not expose generic lists
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

        /// <summary>
        /// Like <see cref="EnsureAllocatedAndPopulated{TExpression}(ref TExpression[], IReadOnlyList{TExpression}, int)"/>,
        /// but where the destination list if is of a derived type
        /// </summary>
        /// <typeparam name="TDestination">The destination list type</typeparam>
        /// <typeparam name="TSource">The source list type</typeparam>
        /// <param name="destination">The destination list to allocate and populate</param>
        /// <param name="source">The source list</param>
        /// <param name="count">The number of elements from source to copy to destination</param>
#pragma warning disable CA1002 // Do not expose generic lists
        protected static void EnsureAllocatedAndPopulatedChangeType<TDestination, TSource>(ref List<TDestination> destination, IReadOnlyList<TSource> source, int count)
#pragma warning restore CA1002 // Do not expose generic lists
            where TSource : Expression
            where TDestination : TSource
        {
            if (destination == null)
            {
                destination = new List<TDestination>();
                for (int j = 0; j < count; j++)
                {
                    destination.Add((TDestination)source[j]);
                }
            }
        }
    }
}
