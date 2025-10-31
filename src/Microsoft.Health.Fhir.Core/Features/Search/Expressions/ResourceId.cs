// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.Core.Features.Search.Expressions
{
    /// <summary>
    /// Represents a single resource ID in the trusted list.
    /// </summary>
    public struct ResourceId : IEquatable<ResourceId>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ResourceId"/> struct.
        /// </summary>
        /// <param name="resourceTypeId">The resource type ID (e.g., 1 for Patient, 2 for Observation)</param>
        /// <param name="resourceSurrogateId">The resource's surrogate ID (internal database ID)</param>
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
        /// Determines whether two specified instances of ResourceId are equal.
        /// </summary>
        public static bool operator ==(ResourceId left, ResourceId right) => left.Equals(right);

        /// <summary>
        /// Determines whether two specified instances of ResourceId are not equal.
        /// </summary>
        public static bool operator !=(ResourceId left, ResourceId right) => !left.Equals(right);

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
    }
}
