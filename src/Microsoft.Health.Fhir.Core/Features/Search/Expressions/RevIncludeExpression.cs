// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search.Expressions
{
    /// <summary>
    /// Represents an include expression (where an additional resource is included based on a reference)
    /// </summary>
    public class RevIncludeExpression : IncludeBaseExpression
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="IncludeExpression"/> class.
        /// </summary>
        /// <param name="resourceType">The resource that supports the reference.</param>
        /// <param name="referenceSearchParameter">THe search parameter that establishes the reference relationship.</param>
        /// <param name="targetResourceType">The target type of the reference.</param>
        /// <param name="wildCard">If this is a wildcard reference include (include all referenced resources).</param>
        public RevIncludeExpression(string resourceType, SearchParameterInfo referenceSearchParameter, string targetResourceType, bool wildCard)
        : base(resourceType, referenceSearchParameter, targetResourceType, wildCard)
        {
        }

        public override TOutput AcceptVisitor<TContext, TOutput>(IExpressionVisitor<TContext, TOutput> visitor, TContext context)
        {
            EnsureArg.IsNotNull(visitor, nameof(visitor));

            return visitor.VisitRevInclude(this, context);
        }

        public override string ToString()
        {
            if (WildCard)
            {
                return "(RevInclude wildcard)";
            }

            var targetType = TargetResourceType != null ? $":{TargetResourceType}" : string.Empty;
            return $"(RevInclude {ReferenceSearchParameter.Name}{targetType})";
        }
    }
}
