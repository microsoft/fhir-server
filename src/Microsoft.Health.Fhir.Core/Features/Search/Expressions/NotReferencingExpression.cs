// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search.Expressions
{
    /// <summary>
    /// Matches resources of a given type that have no outgoing reference for a specific reference
    /// search parameter (e.g. Device resources with no patient reference).
    /// This is the outbound counterpart of <see cref="NotReferencedExpression"/>.
    /// </summary>
    public class NotReferencingExpression : Expression
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="NotReferencingExpression"/> class.
        /// </summary>
        /// <param name="sourceResourceType">The resource type being filtered (the reference source).</param>
        /// <param name="referenceSearchParameter">The reference search parameter that must be absent.</param>
        public NotReferencingExpression(string sourceResourceType, SearchParameterInfo referenceSearchParameter)
        {
            SourceResourceType = EnsureArg.IsNotNullOrEmpty(sourceResourceType, nameof(sourceResourceType));
            ReferenceSearchParameter = EnsureArg.IsNotNull(referenceSearchParameter, nameof(referenceSearchParameter));
        }

        /// <summary>
        /// Gets the resource type being filtered.
        /// </summary>
        public string SourceResourceType { get; }

        /// <summary>
        /// Gets the reference search parameter that must have no indexed values.
        /// </summary>
        public SearchParameterInfo ReferenceSearchParameter { get; }

        public override TOutput AcceptVisitor<TContext, TOutput>(IExpressionVisitor<TContext, TOutput> visitor, TContext context)
        {
            EnsureArg.IsNotNull(visitor, nameof(visitor));

            return visitor.VisitNotReferencing(this, context);
        }

        public override string ToString()
        {
            return $"(NotReferencing {SourceResourceType}.{ReferenceSearchParameter.Code})";
        }

        public override void AddValueInsensitiveHashCode(ref HashCode hashCode)
        {
            hashCode.Add(typeof(NotReferencingExpression));
            hashCode.Add(SourceResourceType, StringComparer.Ordinal);
            hashCode.Add(ReferenceSearchParameter);
        }

        public override bool ValueInsensitiveEquals(Expression other)
        {
            return other is NotReferencingExpression notReferencing &&
                string.Equals(notReferencing.SourceResourceType, SourceResourceType, StringComparison.Ordinal) &&
                notReferencing.ReferenceSearchParameter.Equals(ReferenceSearchParameter);
        }
    }
}
