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
        public DeleteResourceResponse(ResourceKey resourceKey, WeakETag weakETag = null)
        {
            EnsureArg.IsNotNull(resourceKey, nameof(resourceKey));

            ResourceKey = resourceKey;
            WeakETag = weakETag;
        }

        public ResourceKey ResourceKey { get; }

        public WeakETag WeakETag { get; }
    }
}
