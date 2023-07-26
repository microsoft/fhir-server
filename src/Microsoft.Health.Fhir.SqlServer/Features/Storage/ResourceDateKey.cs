// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Features.Persistence
{
    public class ResourceDateKey : IEquatable<ResourceDateKey>
    {
        public ResourceDateKey(short resourceTypeId, string id, long resourceSurrogateId, string versionId, bool isDeleted = false)
        {
            EnsureArg.IsNotNullOrEmpty(id, nameof(id));

            ResourceTypeId = resourceTypeId;
            Id = id;
            ResourceSurrogateId = resourceSurrogateId;
            VersionId = versionId;
            IsDeleted = isDeleted;
        }

        public short ResourceTypeId { get; }

        public string Id { get; }

        public long ResourceSurrogateId { get; }

        public string VersionId { get; }

        public bool IsDeleted { get; }

        public bool Equals(ResourceDateKey other)
        {
            if (ReferenceEquals(null, other))
            {
                return false;
            }

            if (ReferenceEquals(this, other))
            {
                return true;
            }

            return ResourceTypeId == other.ResourceTypeId &&
                   Id == other.Id &&
                   ResourceSurrogateId == other.ResourceSurrogateId &&
                   VersionId == other.VersionId &&
                   IsDeleted == other.IsDeleted;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
            {
                return false;
            }

            if (ReferenceEquals(this, obj))
            {
                return true;
            }

            if (obj.GetType() != GetType())
            {
                return false;
            }

            return Equals((ResourceKey)obj);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(ResourceTypeId, Id, ResourceSurrogateId, VersionId, IsDeleted);
        }
    }
}
