// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Features.Persistence
{
    public class ResourceDateLocationKey : IEquatable<ResourceDateLocationKey>
    {
        public ResourceDateLocationKey(short resourceTypeId, string id, long resourceSurrogateId, string versionId, long? resourceStorageId, int? resourceStorageOffset, int? resourceStorageLength, bool isDeleted = false)
        {
            EnsureArg.IsNotNullOrEmpty(id, nameof(id));

            ResourceTypeId = resourceTypeId;
            Id = id;
            ResourceSurrogateId = resourceSurrogateId;
            VersionId = versionId;
            if (resourceStorageId.HasValue && resourceStorageOffset.HasValue && resourceStorageLength.HasValue)
            {
                RawResourceLocator = new RawResourceLocator(resourceStorageId.Value, resourceStorageOffset.Value, resourceStorageLength.Value);
            }
            else
            {
                RawResourceLocator = null;
            }

            IsDeleted = isDeleted;
        }

        public short ResourceTypeId { get; }

        public string Id { get; }

        public long ResourceSurrogateId { get; }

        public string VersionId { get; }

        public bool IsDeleted { get; }

        public RawResourceLocator RawResourceLocator { get; }

        public bool Equals(ResourceDateLocationKey other)
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
                   RawResourceLocator.Equals(other.RawResourceLocator) &&
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

            return Equals((ResourceDateLocationKey)obj);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(ResourceTypeId, Id, ResourceSurrogateId, VersionId, IsDeleted);
        }
    }
}
