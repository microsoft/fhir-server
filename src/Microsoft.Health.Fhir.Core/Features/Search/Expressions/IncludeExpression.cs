// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search.Expressions
{
    public class IncludeExpression : Expression
    {
        public IncludeExpression(string resourceType, SearchParameterInfo referenceSearchParameter, string targetResourceType, bool wildCard)
        {
            ResourceType = resourceType;
            ReferenceSearchParameter = referenceSearchParameter;
            TargetResourceType = targetResourceType;
            WildCard = wildCard;
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

        public bool WildCard { get; }

        public override TOutput AcceptVisitor<TContext, TOutput>(IExpressionVisitor<TContext, TOutput> visitor, TContext context)
        {
            EnsureArg.IsNotNull(visitor, nameof(visitor));

            return visitor.VisitInclude(this, context);
        }

        public override string ToString()
        {
            return $"(Include {ReferenceSearchParameter.Name}:{TargetResourceType})";
        }
    }
}
