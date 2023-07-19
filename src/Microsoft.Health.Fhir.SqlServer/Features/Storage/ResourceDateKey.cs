// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Persistence
{
    public class ResourceDateKey : IEquatable<ResourceDateKey>
    {
        public ResourceDateKey(string resourceType, string id, long resourceSurrogateId, string versionId, bool isDeleted = false)
        {
            EnsureArg.IsNotNullOrEmpty(resourceType, nameof(resourceType));
            EnsureArg.IsNotNullOrEmpty(id, nameof(id));
            EnsureArg.IsTrue(ModelInfoProvider.IsKnownResource(resourceType), nameof(resourceType));

            ResourceType = resourceType;
            Id = id;
            ResourceSurrogateId = resourceSurrogateId;
            VersionId = versionId;
            IsDeleted = isDeleted;
        }

        public string ResourceType { get; }

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

            return ResourceType == other.ResourceType &&
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
            return HashCode.Combine(ResourceType, Id, ResourceSurrogateId, VersionId, IsDeleted);
        }
    }
}
