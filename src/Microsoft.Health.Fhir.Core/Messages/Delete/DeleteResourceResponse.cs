// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Persistence;

namespace Microsoft.Health.Fhir.Core.Messages.Delete
{
    public class DeleteResourceResponse
    {
        public DeleteResourceResponse(ResourceKey resourceKey, int resourcesDeleted = 1, WeakETag weakETag = null)
        {
            EnsureArg.IsNotNull(resourceKey, nameof(resourceKey));

            ResourceKey = resourceKey;
            ResourcesDeleted = resourcesDeleted;
            WeakETag = weakETag;
        }

        public DeleteResourceResponse(int resourcesDeleted)
        {
            ResourcesDeleted = resourcesDeleted;
        }

        public ResourceKey ResourceKey { get; }

        public WeakETag WeakETag { get; }

        public int ResourcesDeleted { get; }
    }
}
