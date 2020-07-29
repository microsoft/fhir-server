// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Persistence;

namespace Microsoft.Health.Fhir.Core.Messages.Get
{
    public class GetResourceResponse
    {
        public GetResourceResponse(ResourceWrapper resource)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));

            Resource = resource;
        }

        public ResourceWrapper Resource { get; }
    }
}
