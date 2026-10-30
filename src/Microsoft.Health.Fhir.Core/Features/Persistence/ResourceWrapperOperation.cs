// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
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
            BundleResourceContext bundleResourceContext,
            bool metaHistory = true,
            bool createOnly = false)
        {
            Wrapper = EnsureArg.IsNotNull(wrapper, nameof(wrapper));
            AllowCreate = allowCreate;
            KeepHistory = keepHistory;
            WeakETag = weakETag; // weakETag can be null.
            RequireETagOnUpdate = requireETagOnUpdate;
            KeepVersion = keepVersion;
            BundleResourceContext = bundleResourceContext;
            MetaHistory = metaHistory;
            CreateOnly = createOnly;
        }

        public ResourceWrapper Wrapper { get; }

        public bool AllowCreate { get; }

        public bool KeepHistory { get; }

        public WeakETag WeakETag { get; }

        public bool RequireETagOnUpdate { get; }

        public bool KeepVersion { get; }

        public bool MetaHistory { get; }

        /// <summary>
        /// When true, the write must not replace a live (non-deleted) resource with the same id; it fails with a conflict instead.
        /// Used by conditional update when the search criteria found no match but the client supplied an id.
        /// </summary>
        public bool CreateOnly { get; }

        public BundleResourceContext BundleResourceContext { get; }

        public ResourceSearchParameterStatus PendingSearchParameterStatus { get; internal set; }

#pragma warning disable CA1024 // Use properties where appropriate
        public DataStoreOperationIdentifier GetIdentifier()
        {
            return new DataStoreOperationIdentifier(this);
        }
#pragma warning restore CA1024 // Use properties where appropriate
    }
}
