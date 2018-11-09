// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Hl7.Fhir.Model;

namespace Microsoft.Health.Fhir.Core.Features.Search.Expressions
{
    /// <summary>
    /// Represents a chained expression (where the child expression is chained to another resource.)
    /// </summary>
    public class ChainedExpression : SearchParameterExpression
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ChainedExpression"/> class.
        /// </summary>
        /// <param name="searchParameter">The search parameter</param>
        /// <param name="resourceType">The resource type that supports this search expression.</param>
        /// <param name="targetResourceType">The target resource type.</param>
        /// <param name="expression">The search expression.</param>
        public ChainedExpression(
            SearchParameter searchParameter,
            ResourceType resourceType,
            ResourceType targetResourceType,
            Expression expression)
        : base(searchParameter, expression)
        {
            EnsureArg.IsTrue(Enum.IsDefined(typeof(ResourceType), resourceType), nameof(resourceType));
            EnsureArg.IsTrue(Enum.IsDefined(typeof(ResourceType), targetResourceType), nameof(targetResourceType));
            EnsureArg.IsNotNull(expression, nameof(expression));

            ResourceType = resourceType;
            TargetResourceType = targetResourceType;
        }

        /// <summary>
        /// Gets the resource type which is being searched.
        /// </summary>
        public ResourceType ResourceType { get; }

        /// <summary>
        /// Gets the target resource type.
        /// </summary>
        public ResourceType TargetResourceType { get; }

        protected internal override void AcceptVisitor(IExpressionVisitor visitor)
        {
            EnsureArg.IsNotNull(visitor, nameof(visitor));

            visitor.Visit(this);
        }
    }
}
