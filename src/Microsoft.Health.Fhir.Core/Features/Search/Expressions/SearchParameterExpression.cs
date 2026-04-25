// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Core.Features.Search.Expressions
{
    /// <summary>
    /// Represents a set of ANDed expressions over a search parameter.
    /// </summary>
    public class SearchParameterExpression : SearchParameterExpressionBase
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SearchParameterExpression"/> class.
        /// </summary>
        /// <param name="searchParameter">The search parameter this expression is bound to.</param>
        /// <param name="expression">The expression over the parameter's values.</param>
        /// <param name="comparator">The original comparator for the parsed search value, if known.</param>
        public SearchParameterExpression(SearchParameterInfo searchParameter, Expression expression, SearchComparator? comparator = null)
            : base(searchParameter)
        {
            EnsureArg.IsNotNull(expression, nameof(expression));

            Expression = expression;
            Comparator = comparator;
        }

        /// <summary>
        /// Gets the expression over the parameter's values.
        /// </summary>
        public Expression Expression { get; }

        /// <summary>
        /// Gets the original comparator for the parsed search value, if known.
        /// </summary>
        public SearchComparator? Comparator { get; }

        public override TOutput AcceptVisitor<TContext, TOutput>(IExpressionVisitor<TContext, TOutput> visitor, TContext context)
        {
            EnsureArg.IsNotNull(visitor, nameof(visitor));
            return visitor.VisitSearchParameter(this, context);
        }

        public override string ToString()
        {
            return $"(Param {Parameter.Code} {Expression})";
        }

        public override void AddValueInsensitiveHashCode(ref HashCode hashCode)
        {
            hashCode.Add(typeof(SearchParameterExpression));
            hashCode.Add(Parameter);
            hashCode.Add(Comparator);
            Expression.AddValueInsensitiveHashCode(ref hashCode);
        }

        public override bool ValueInsensitiveEquals(Expression other)
        {
            return other is SearchParameterExpression spe &&
                   spe.Parameter.Equals(Parameter) &&
                   spe.Comparator == Comparator &&
                   spe.Expression.ValueInsensitiveEquals(Expression);
        }
    }
}
