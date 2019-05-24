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
        /// <param name="paramName">The search parameter name.</param>
        /// <param name="targetResourceType">The target resource type.</param>
        /// <param name="expression">The search expression.</param>
        public ChainedExpression(
            string resourceType,
            string paramName,
            string targetResourceType,
            Expression expression)
        {
            EnsureArg.IsTrue(ModelInfoProvider.IsKnownResource(resourceType), nameof(resourceType));
            EnsureArg.IsNotNullOrWhiteSpace(paramName, nameof(paramName));
            EnsureArg.IsTrue(ModelInfoProvider.IsKnownResource(targetResourceType), nameof(targetResourceType));
            EnsureArg.IsNotNull(expression, nameof(expression));

            ResourceType = resourceType;
            ParamName = paramName;
            TargetResourceType = targetResourceType;
            Expression = expression;
        }

        /// <summary>
        /// Gets the resource type which is being searched.
        /// </summary>
        public string ResourceType { get; }

        /// <summary>
        /// Gets the parameter name.
        /// </summary>
        public string ParamName { get; }

        /// <summary>
        /// Gets the target resource type.
        /// </summary>
        public string TargetResourceType { get; }

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
            return $"(Chain {ParamName}:{TargetResourceType} {Expression})";
        }
    }
}
