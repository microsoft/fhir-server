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
            ResourceStorageId = resourceStorageId.HasValue ? resourceStorageId.Value : 0;
            ResourceStorageOffset = resourceStorageOffset.HasValue ? resourceStorageOffset.Value : 0;
            ResourceStorageLength = resourceStorageLength.HasValue ? resourceStorageLength.Value : 0;
            IsDeleted = isDeleted;
        }

        public short ResourceTypeId { get; }

        public string Id { get; }

        public long ResourceSurrogateId { get; }

        public string VersionId { get; }

        public bool IsDeleted { get; }

        public long ResourceStorageId { get; }

        public int ResourceStorageOffset { get; }

        public int ResourceStorageLength { get; }

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
                   ResourceStorageId == other.ResourceStorageId &&
                   ResourceStorageOffset == other.ResourceStorageOffset &&
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
            // TODO: Should ResourceStorageId and Offset be included?
            return HashCode.Combine(ResourceTypeId, Id, ResourceSurrogateId, VersionId, IsDeleted);
        }
    }
}
