// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Persistence
{
    public class ResourceWrapperExtended
    {
        public ResourceWrapperExtended(
            ResourceWrapper wrapper,
            bool allowCreate,
            bool keepHistory,
            WeakETag weakETag,
            bool requireETagOnUpdate)
        {
            Wrapper = wrapper;
            AllowCreate = allowCreate;
            KeepHistory = keepHistory;
            WeakETag = weakETag;
            RequireETagOnUpdate = requireETagOnUpdate;
        }

        public ResourceWrapper Wrapper { get; private set; }

        public bool AllowCreate { get; private set; }

        public bool KeepHistory { get; private set; }

        public WeakETag WeakETag { get; private set; }

        public bool RequireETagOnUpdate { get; private set; }
    }
}
