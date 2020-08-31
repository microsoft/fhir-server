// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search.Expressions
{
    /// <summary>
    /// Represents an include expression (where an additional resource is included based on a reference)
    /// </summary>
    public class IncludeExpression : Expression
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="IncludeExpression"/> class.
        /// </summary>
        /// <param name="resourceType">The resource that supports the reference.</param>
        /// <param name="referenceSearchParameter">THe search parameter that establishes the reference relationship.</param>
        /// <param name="targetResourceType">The target type of the reference.</param>
        /// <param name="wildCard">If this is a wildcard reference include (include all referenced resources).</param>
        /// <param name="reversed">If this is a reversed include (revinclude) expression.</param>
        /// <param name="iterate"> If :iterate (:recurse) modifer was applied.</param>
        public IncludeExpression(string resourceType, SearchParameterInfo referenceSearchParameter, string targetResourceType, bool wildCard, bool reversed, bool iterate)
        {
            EnsureArg.IsNotNullOrWhiteSpace(resourceType, nameof(resourceType));

            if (!wildCard)
            {
                EnsureArg.IsNotNull(referenceSearchParameter, nameof(referenceSearchParameter));
            }

            ResourceType = resourceType;
            ReferenceSearchParameter = referenceSearchParameter;
            TargetResourceType = targetResourceType;
            WildCard = wildCard;
            Reversed = reversed;
            Iterate = iterate;
            SetRecursive();
        }

        /// <summary>
        /// Gets the resource type which is being searched.
        /// </summary>
        public string ResourceType { get; }

        /// <summary>
        /// Gets the reference search parameter for the relationship.
        /// </summary>
        public SearchParameterInfo ReferenceSearchParameter { get; }

        /// <summary>
        /// Gets the target resource type. Value will be null if none are specified.
        /// </summary>
        public string TargetResourceType { get; }

        /// <summary>
        /// Gets if the include is a wildcard include.
        /// </summary>
        public bool WildCard { get; }

        /// Get if the expression is reversed.
        /// </summary>
        public bool Reversed { get; }

        /// <summary>
        /// Gets if the include has :iterate (:recurse) modifier.
        /// </summary>
        public bool Iterate { get; }

        /// <summary>
        /// Gets if the include is recursive (i.e., circular reference: target reference is of the same resource type, e.g., Organization:partof)
        /// </summary>
        public bool Recursive { get; private set; }

        public override TOutput AcceptVisitor<TContext, TOutput>(IExpressionVisitor<TContext, TOutput> visitor, TContext context)
        {
            EnsureArg.IsNotNull(visitor, nameof(visitor));

            return visitor.VisitInclude(this, context);
        }

        public override string ToString()
        {
            if (WildCard)
            {
                return "(Include wildcard)";
            }

            var targetType = TargetResourceType != null ? $":{TargetResourceType}" : string.Empty;
            var iterate = Iterate ? "Iterate" : string.Empty;
            var reversed = Reversed ? "Reversed" : string.Empty;
            return $"({reversed} Include {iterate} {ReferenceSearchParameter.Name}{targetType})";
        }

        /// <summary>
        /// Returns if the include expression is Recursive (target reference is of the same as base resource type, e.g., Organization:partof)
        private void SetRecursive()
        {
            Recursive = false;

            if (TargetResourceType != null)
            {
                Recursive = ReferenceSearchParameter != null && ReferenceSearchParameter.IsBaseResourceType(TargetResourceType);
            }
            else if (ReferenceSearchParameter?.TargetResourceTypes != null)
            {
                // Todo (limorl) Use Intersect / Contains
                foreach (var t in ReferenceSearchParameter.TargetResourceTypes)
                {
                    // if (ReferenceSearchParameter.IsBaseResourceType(t))
                    if (ResourceType == t)
                    {
                        Recursive = true;
                        return;
                    }
                }
            }
        }
    }
}
