// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
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
        /// <param name="resourceTypes">The resource type that supports this search expression.</param>
        /// <param name="referenceSearchParameter">The search parameter that establishes the reference</param>
        /// <param name="targetResourceTypes">The target resource type.</param>
        /// <param name="reversed">If this is a reversed chained expression.</param>
        /// <param name="expression">The search expression.</param>
        public ChainedExpression(
            string[] resourceTypes,
            SearchParameterInfo referenceSearchParameter,
            string[] targetResourceTypes,
            bool reversed,
            Expression expression)
        {
            EnsureArg.IsNotNull(resourceTypes, nameof(resourceTypes));
            EnsureArg.IsTrue(resourceTypes.All(x => ModelInfoProvider.IsKnownResource(x)), nameof(resourceTypes));
            EnsureArg.IsNotNull(referenceSearchParameter, nameof(referenceSearchParameter));
            EnsureArg.IsNotNull(targetResourceTypes, nameof(targetResourceTypes));
            EnsureArg.IsTrue(targetResourceTypes.All(x => ModelInfoProvider.IsKnownResource(x)), nameof(targetResourceTypes));
            EnsureArg.IsNotNull(expression, nameof(expression));

            ResourceTypes = resourceTypes;
            ReferenceSearchParameter = referenceSearchParameter;
            TargetResourceTypes = targetResourceTypes;
            Reversed = reversed;
            Expression = expression;
        }

        /// <summary>
        /// Gets the resource type which is being searched.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "Array property")]
        public string[] ResourceTypes { get; }

        /// <summary>
        /// Gets the parameter name.
        /// </summary>
        public SearchParameterInfo ReferenceSearchParameter { get; }

        /// <summary>
        /// Gets the target resource type.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "Array property")]
        public string[] TargetResourceTypes { get; }

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
            return $"({(Reversed ? "Reverse " : string.Empty)}Chain {ReferenceSearchParameter.Code}:{string.Join(", ", TargetResourceTypes)} {Expression})";
        }
    }
}
