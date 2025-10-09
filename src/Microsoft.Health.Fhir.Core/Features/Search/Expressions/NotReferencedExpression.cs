// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search.Expressions
{
    /// <summary>
    /// Handles searching for resources that have no references to them.
    /// Currently this only supports searching for resources that are have no references to them, but in the future could be enhanced to look for resources that don't have specific references to them.
    /// </summary>
    public class NotReferencedExpression : Expression
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="IncludeExpression"/> class.
        /// </summary>
        /// <param name="referenceSearchParameter">The search parameter that establishes the reference relationship.</param>
        /// <param name="sourceResourceType">The source type of the reference.</param>
        /// <param name="wildCard">If this is a wildcard reference include (include all referenced resources).</param>
        public NotReferencedExpression(
            SearchParameterInfo referenceSearchParameter,
            string sourceResourceType,
            bool wildCard)
        {
            if (!wildCard)
            {
                EnsureArg.IsNotNull(referenceSearchParameter, nameof(referenceSearchParameter));
                EnsureArg.IsNotNullOrEmpty(sourceResourceType, nameof(sourceResourceType));
            }

            ReferenceSearchParameter = referenceSearchParameter;
            SourceResourceType = sourceResourceType;
            WildCard = wildCard;
        }

        /// <summary>
        /// Gets the reference search parameter for the relationship.
        /// </summary>
        public SearchParameterInfo ReferenceSearchParameter { get; }

        /// <summary>
        /// Gets the source resource type. Value will be null if none are specified.
        /// </summary>
        public string SourceResourceType { get; }

        /// <summary>
        /// Gets if the expression is a wildcard include.
        /// </summary>
        public bool WildCard { get; }

        public override TOutput AcceptVisitor<TContext, TOutput>(IExpressionVisitor<TContext, TOutput> visitor, TContext context)
        {
            EnsureArg.IsNotNull(visitor, nameof(visitor));

            return visitor.VisitNotReferenced(this, context);
        }

        public override string ToString()
        {
            var wildcard = WildCard ? " any" : string.Empty;
            var searchParameter = ReferenceSearchParameter == null ? string.Empty : "." + ReferenceSearchParameter.Name;
            var resourceType = SourceResourceType == null ? string.Empty : " " + SourceResourceType;
            return $"Not Referenced by{wildcard}{resourceType}{searchParameter}";
        }

        public override void AddValueInsensitiveHashCode(ref HashCode hashCode)
        {
            hashCode.Add(typeof(NotReferencedExpression));
        }

        public override bool ValueInsensitiveEquals(Expression other)
        {
            return other is NotReferencedExpression;
        }
    }
}
