// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Persistence;

namespace Microsoft.Health.Fhir.Core.Messages.Delete
{
    public class DeleteResourceResponse
    {
        public DeleteResourceResponse(WeakETag weakETag = null)
        {
            WeakETag = weakETag;
        }

        public WeakETag WeakETag { get; }
    }
}