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
                  resourceWrapperOperation.RequireETagOnUpdate,
                  resourceWrapperOperation.ComparedVersion)
        {
        }

        public DataStoreOperationIdentifier(
            string id,
            string resourceType,
            string version,
            bool allowCreate,
            bool keepHistory,
            WeakETag weakETag,
            bool requireETagOnUpdate,
            string comparedVersion = null)
         : base(resourceType, id, version)
        {
            AllowCreate = allowCreate;
            KeepHistory = keepHistory;
            WeakETag = weakETag; // Can be null.
            RequireETagOnUpdate = requireETagOnUpdate;
            ComparedVersion = comparedVersion;
        }

        public bool AllowCreate { get; }

        public bool KeepHistory { get; }

        public WeakETag WeakETag { get; }

        public bool RequireETagOnUpdate { get; }

        /// <summary>
        /// Gets the resource version observed while resolving a conditional operation.
        /// </summary>
        public string ComparedVersion { get; }

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
                   RequireETagOnUpdate == other.RequireETagOnUpdate &&
                   string.Equals(ComparedVersion, other.ComparedVersion, StringComparison.Ordinal);
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
            return HashCode.Combine(base.GetHashCode(), AllowCreate, KeepHistory, WeakETag, RequireETagOnUpdate, ComparedVersion);
        }
    }
}
