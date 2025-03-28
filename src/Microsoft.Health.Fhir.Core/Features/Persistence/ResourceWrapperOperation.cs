// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Health.Fhir.Core.Models;

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
            bool keepVersion,
            BundleResourceContext bundleResourceContext)
        {
            Wrapper = EnsureArg.IsNotNull(wrapper, nameof(wrapper));
            AllowCreate = allowCreate;
            KeepHistory = keepHistory;
            WeakETag = weakETag; // weakETag can be null.
            RequireETagOnUpdate = requireETagOnUpdate;
            KeepVersion = keepVersion;
            BundleResourceContext = bundleResourceContext;
        }

        public ResourceWrapper Wrapper { get; }

        public bool AllowCreate { get; }

        public bool KeepHistory { get; }

        public WeakETag WeakETag { get; }

        public bool RequireETagOnUpdate { get; }

        public bool KeepVersion { get; }

        public BundleResourceContext BundleResourceContext { get; }

#pragma warning disable CA1024 // Use properties where appropriate
        public DataStoreOperationIdentifier GetIdentifier()
        {
            return new DataStoreOperationIdentifier(this);
        }
#pragma warning restore CA1024 // Use properties where appropriate
    }
}
