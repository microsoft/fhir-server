// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Features.Persistence
{
    public class ResourceWrapperOperation
    {
        public ResourceWrapperOperation(
            ResourceWrapper wrapper,
            bool allowCreate,
            bool keepHistory,
            WeakETag weakETag,
            bool requireETagOnUpdate,
            Guid? bundleOperationId)
        {
            Wrapper = EnsureArg.IsNotNull(wrapper, nameof(wrapper));
            AllowCreate = allowCreate;
            KeepHistory = keepHistory;
            WeakETag = weakETag; // weakETag can be null.
            RequireETagOnUpdate = requireETagOnUpdate;
            BundleOperationId = bundleOperationId;
        }

        public ResourceWrapper Wrapper { get; }

        public bool AllowCreate { get; }

        public bool KeepHistory { get; }

        public WeakETag WeakETag { get; }

        public bool RequireETagOnUpdate { get; }

        public Guid? BundleOperationId { get; }

        public DataStoreOperationIdentifier GetIdentifier()
        {
            /// BundleOperationId does not need to be part of <see cref="DataStoreOperationIdentifier"/>.

            ResourceKey resourceKey = Wrapper.ToResourceKey();
            return new DataStoreOperationIdentifier(resourceKey, AllowCreate, KeepHistory, WeakETag, RequireETagOnUpdate);
        }
    }
}
