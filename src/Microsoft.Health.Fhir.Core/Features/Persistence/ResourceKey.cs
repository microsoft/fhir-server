// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Text;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Persistence
{
    public class ResourceKey : IEquatable<ResourceKey>, IComparable<ResourceKey>
    {
        public ResourceKey(string resourceType, string id, string versionId = null)
        {
            EnsureArg.IsNotNullOrEmpty(resourceType, nameof(resourceType));
            EnsureArg.IsNotNullOrEmpty(id, nameof(id));
            EnsureArg.IsTrue(ModelInfoProvider.IsKnownResource(resourceType), nameof(resourceType));

            Id = id;
            VersionId = versionId;
            ResourceType = resourceType;
        }

        public string Id { get; }

        public string VersionId { get; }

        public string ResourceType { get; }

        public static bool operator ==(ResourceKey left, ResourceKey right)
        {
            if (ReferenceEquals(left, null))
            {
                return ReferenceEquals(right, null);
            }

            return left.Equals(right);
        }

        public static bool operator !=(ResourceKey left, ResourceKey right)
        {
            return !(left == right);
        }

        public static bool operator <(ResourceKey left, ResourceKey right)
        {
            return ReferenceEquals(left, null) ? !ReferenceEquals(right, null) : left.CompareTo(right) < 0;
        }

        public static bool operator <=(ResourceKey left, ResourceKey right)
        {
            return ReferenceEquals(left, null) || left.CompareTo(right) <= 0;
        }

        public static bool operator >(ResourceKey left, ResourceKey right)
        {
            return !ReferenceEquals(left, null) && left.CompareTo(right) > 0;
        }

        public static bool operator >=(ResourceKey left, ResourceKey right)
        {
            return ReferenceEquals(left, null) ? ReferenceEquals(right, null) : left.CompareTo(right) >= 0;
        }

        public bool Equals(ResourceKey other)
        {
            if (ReferenceEquals(null, other))
            {
                return false;
            }

            if (ReferenceEquals(this, other))
            {
                return true;
            }

            return Id == other.Id &&
                   VersionId == other.VersionId &&
                   ResourceType == other.ResourceType;
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
            return HashCode.Combine(Id, VersionId, ResourceType);
        }

        public override string ToString()
        {
            var builder = new StringBuilder();

            builder.AppendFormat("{0}/{1}", ResourceType, Id);

            if (!string.IsNullOrEmpty(VersionId))
            {
                builder.AppendFormat("/_history/{0}", VersionId);
            }

            return builder.ToString();
        }

        public int CompareTo(ResourceKey other)
        {
            int result = 0;
            if (!string.Equals(ResourceType, other.ResourceType, StringComparison.OrdinalIgnoreCase))
            {
                result = string.Compare(ResourceType, other.ResourceType, StringComparison.OrdinalIgnoreCase);
            }
            else if (!string.Equals(Id, other.Id, StringComparison.OrdinalIgnoreCase))
            {
                result = string.Compare(Id, other.Id, StringComparison.OrdinalIgnoreCase);
            }
            else if (VersionId != null && other.VersionId != null)
            {
                var versionIsNum = int.TryParse(VersionId, out int version);
                var otherVersionIsNum = int.TryParse(other.VersionId, out int otherVersion);
                if (versionIsNum && otherVersionIsNum)
                {
                    result = version.CompareTo(otherVersion);
                }
                else
                {
                    result = string.Compare(VersionId, other.VersionId, StringComparison.OrdinalIgnoreCase);
                }
            }
            else
            {
                result = 0;
            }

            return result;
        }
    }
}
