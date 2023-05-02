// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Features.Persistence
{
    public sealed class DataStoreOperationIdentifier
    {
        public DataStoreOperationIdentifier(
            ResourceKey resourceKey,
            bool allowCreate,
            bool keepHistory,
            WeakETag weakETag,
            bool requireETagOnUpdate)
        {
            ResourceKey = EnsureArg.IsNotNull(resourceKey, nameof(resourceKey));
            AllowCreate = allowCreate;
            KeepHistory = keepHistory;
            WeakETag = weakETag;
            RequireETagOnUpdate = requireETagOnUpdate;
        }

        public ResourceKey ResourceKey { get; }

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

            return ResourceKey.Equals(other.ResourceKey) &&
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
            return HashCode.Combine(ResourceKey, AllowCreate, KeepHistory, WeakETag, RequireETagOnUpdate);
        }
    }
}
