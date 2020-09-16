// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search.Expressions
{
    /// <summary>
    /// Represents a chained expression (where the child expression is chained to another resource.)
    /// </summary>
    public class ChainedExpression : Expression
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ChainedExpression"/> class.
        /// </summary>
        /// <param name="resourceType">The resource type that supports this search expression.</param>
        /// <param name="referenceSearchParameter">The search parameter that establishes the reference</param>
        /// <param name="targetResourceType">The target resource type.</param>
        /// <param name="reversed">If this is a reversed chained expression.</param>
        /// <param name="expression">The search expression.</param>
        public ChainedExpression(
            string resourceType,
            SearchParameterInfo referenceSearchParameter,
            string targetResourceType,
            bool reversed,
            Expression expression)
        {
            EnsureArg.IsTrue(ModelInfoProvider.IsKnownResource(resourceType), nameof(resourceType));
            EnsureArg.IsNotNull(referenceSearchParameter, nameof(referenceSearchParameter));
            EnsureArg.IsTrue(ModelInfoProvider.IsKnownResource(targetResourceType), nameof(targetResourceType));
            EnsureArg.IsNotNull(expression, nameof(expression));

            ResourceType = resourceType;
            ReferenceSearchParameter = referenceSearchParameter;
            TargetResourceType = targetResourceType;
            Reversed = reversed;
            Expression = expression;
        }

        /// <summary>
        /// Gets the resource type which is being searched.
        /// </summary>
        public string ResourceType { get; }

        /// <summary>
        /// Gets the parameter name.
        /// </summary>
        public SearchParameterInfo ReferenceSearchParameter { get; }

        /// <summary>
        /// Gets the target resource type.
        /// </summary>
        public string TargetResourceType { get; }

        /// <summary>
        /// Get if the expression is reversed.
        /// </summary>
        public bool Reversed { get; }

        /// <summary>
        /// Gets the search expression.
        /// </summary>
        public Expression Expression { get; }

        public override TOutput AcceptVisitor<TContext, TOutput>(IExpressionVisitor<TContext, TOutput> visitor, TContext context)
        {
            EnsureArg.IsNotNull(visitor, nameof(visitor));

            return visitor.VisitChained(this, context);
        }

        public override string ToString()
        {
            return $"({(Reversed ? "Reverse " : string.Empty)}Chain {ReferenceSearchParameter.Name}:{TargetResourceType} {Expression})";
        }
    }
}
