// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Features.Persistence
{
    public sealed class DataStoreOperationIdentifier : ResourceKey, IEquatable<DataStoreOperationIdentifier>
    {
        public DataStoreOperationIdentifier(ResourceWrapperOperation resourceWrapperOperation)
            : this(
                  EnsureArg.IsNotNull(resourceWrapperOperation, nameof(resourceWrapperOperation)).Wrapper.ResourceId,
                  resourceWrapperOperation.Wrapper.ResourceTypeName,
                  resourceWrapperOperation.Wrapper.Version,
                  resourceWrapperOperation.AllowCreate,
                  resourceWrapperOperation.KeepHistory,
                  resourceWrapperOperation.WeakETag,
                  resourceWrapperOperation.RequireETagOnUpdate)
        {
        }

        public DataStoreOperationIdentifier(
            string id,
            string resourceType,
            string version,
            bool allowCreate,
            bool keepHistory,
            WeakETag weakETag,
            bool requireETagOnUpdate)
         : base(resourceType, id, version)
        {
            AllowCreate = allowCreate;
            KeepHistory = keepHistory;
            WeakETag = weakETag; // Can be null.
            RequireETagOnUpdate = requireETagOnUpdate;
        }

        public bool AllowCreate { get; }

        public bool KeepHistory { get; }

        public WeakETag WeakETag { get; }

        public bool RequireETagOnUpdate { get; }

        public bool Equals(DataStoreOperationIdentifier other)
        {
            if (ReferenceEquals(null, other))
            {
                return false;
            }

            if (ReferenceEquals(this, other))
            {
                return true;
            }

            return base.Equals(other) &&
                   AllowCreate == other.AllowCreate &&
                   KeepHistory == other.KeepHistory &&
                   WeakETag == other.WeakETag &&
                   RequireETagOnUpdate == other.RequireETagOnUpdate;
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

            if (GetType() != obj.GetType())
            {
                return false;
            }

            return Equals((DataStoreOperationIdentifier)obj);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(base.GetHashCode(), AllowCreate, KeepHistory, WeakETag, RequireETagOnUpdate);
        }
    }
}
