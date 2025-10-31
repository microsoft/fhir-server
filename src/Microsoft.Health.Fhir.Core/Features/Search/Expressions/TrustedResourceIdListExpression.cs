// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Features.Search.Expressions
{
    /// <summary>
    /// Represents a list of resource IDs that have already been validated/filtered by the caller.
    /// These resources don't need additional compartment, smart compartment, or scope filtering.
    ///
    /// This expression type is used in the two-query approach for granular scope searches:
    /// 1. First query finds matching resources with all filters applied → produces trusted IDs
    /// 2. Second query uses TrustedResourceIdListExpression as the starting point for includes/revinclude
    /// 3. Scope filters are applied ONLY to the included resources, not the starting trusted IDs
    ///
    /// The "trusted" designation means the IDs have already been validated by:
    /// - Compartment restrictions (if applicable)
    /// - Smart compartment restrictions (if applicable)
    /// - SMART scope restrictions (if applicable)
    /// </summary>
    public class TrustedResourceIdListExpression : Expression
    {
        /// <summary>
        /// Represents a single resource ID in the trusted list.
        /// </summary>
        [SuppressMessage("Design", "CA1034:NestedTypesShouldNotBeVisible", Justification = "This is a value type that represents a resource ID pair, commonly used with TrustedResourceIdListExpression")]
        [SuppressMessage("StyleCop.CSharp.OrderingRules", "SA1201:ElementsMustAppearInTheCorrectOrder", Justification = "Constructor follows struct definition per struct conventions")]
        public struct ResourceId : IEquatable<ResourceId>
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="ResourceId"/> struct.
            /// </summary>
            /// <param name="resourceTypeId">The resource type ID (e.g., 1 for Patient, 2 for Observation)</param>
            /// <param name="resourceSurrogateId">The resource's surrogate ID (internal database ID)</param>
            [SuppressMessage("StyleCop.CSharp.OrderingRules", "SA1201:ElementsMustAppearInTheCorrectOrder", Justification = "Constructor must appear after struct keyword and before properties")]
            public ResourceId(short resourceTypeId, long resourceSurrogateId)
            {
                ResourceTypeId = resourceTypeId;
                ResourceSurrogateId = resourceSurrogateId;
            }

            /// <summary>
            /// Gets the resource type ID (e.g., 1 for Patient, 2 for Observation)
            /// </summary>
            public short ResourceTypeId { get; }

            /// <summary>
            /// Gets the resource's surrogate ID (internal database ID)
            /// </summary>
            public long ResourceSurrogateId { get; }

            /// <summary>
            /// Determines whether the specified object is equal to the current object.
            /// </summary>
            public override bool Equals(object obj)
            {
                return obj is ResourceId other && Equals(other);
            }

            /// <summary>
            /// Determines whether the specified ResourceId is equal to the current ResourceId.
            /// </summary>
            public bool Equals(ResourceId other)
            {
                return ResourceTypeId == other.ResourceTypeId && ResourceSurrogateId == other.ResourceSurrogateId;
            }

            /// <summary>
            /// Returns the hash code for this instance.
            /// </summary>
            public override int GetHashCode()
            {
                return HashCode.Combine(ResourceTypeId, ResourceSurrogateId);
            }

            /// <summary>
            /// Determines whether two specified instances of ResourceId are equal.
            /// </summary>
            [SuppressMessage("StyleCop.CSharp.OrderingRules", "SA1201:ElementsMustAppearInTheCorrectOrder", Justification = "Operators must follow methods in struct")]
            public static bool operator ==(ResourceId left, ResourceId right) => left.Equals(right);

            /// <summary>
            /// Determines whether two specified instances of ResourceId are not equal.
            /// </summary>
            [SuppressMessage("StyleCop.CSharp.OrderingRules", "SA1201:ElementsMustAppearInTheCorrectOrder", Justification = "Operators must follow methods in struct")]
            public static bool operator !=(ResourceId left, ResourceId right) => !left.Equals(right);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="TrustedResourceIdListExpression"/> class.
        /// </summary>
        /// <param name="resourceIds">List of resource IDs that have already been validated/filtered</param>
        [SuppressMessage("StyleCop.CSharp.OrderingRules", "SA1201:ElementsMustAppearInTheCorrectOrder", Justification = "Constructor must appear after nested struct")]
        public TrustedResourceIdListExpression(IEnumerable<ResourceId> resourceIds)
        {
            EnsureArg.IsNotNull(resourceIds, nameof(resourceIds));

            ResourceIds = resourceIds.ToList().AsReadOnly();

            if (ResourceIds.Count == 0)
            {
                throw new ArgumentException("TrustedResourceIdListExpression must contain at least one resource ID", nameof(resourceIds));
            }
        }

        /// <summary>
        /// Gets the list of resource IDs that have already been validated/filtered.
        /// These don't need additional compartment or scope filtering.
        /// </summary>
        public IReadOnlyList<ResourceId> ResourceIds { get; }

        public override TOutput AcceptVisitor<TContext, TOutput>(IExpressionVisitor<TContext, TOutput> visitor, TContext context)
        {
            EnsureArg.IsNotNull(visitor, nameof(visitor));

            return visitor.VisitTrustedResourceIdList(this, context);
        }

        public override string ToString()
        {
            var idSummary = ResourceIds.Count <= 3
                ? string.Join(", ", ResourceIds.Select(r => $"{r.ResourceTypeId}:{r.ResourceSurrogateId}"))
                : $"{ResourceIds.Count} IDs";

            return $"(TrustedResourceIdList: {idSummary})";
        }

        public override void AddValueInsensitiveHashCode(ref HashCode hashCode)
        {
            hashCode.Add(typeof(TrustedResourceIdListExpression));
            foreach (var resourceId in ResourceIds)
            {
                hashCode.Add(resourceId.ResourceTypeId);
                hashCode.Add(resourceId.ResourceSurrogateId);
            }
        }

        public override bool ValueInsensitiveEquals(Expression other)
        {
            if (other is not TrustedResourceIdListExpression trustedOther ||
                trustedOther.ResourceIds.Count != ResourceIds.Count)
            {
                return false;
            }

            for (var i = 0; i < ResourceIds.Count; i++)
            {
                if (trustedOther.ResourceIds[i].ResourceTypeId != ResourceIds[i].ResourceTypeId ||
                    trustedOther.ResourceIds[i].ResourceSurrogateId != ResourceIds[i].ResourceSurrogateId)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
