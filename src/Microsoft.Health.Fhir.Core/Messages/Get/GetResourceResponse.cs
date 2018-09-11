// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Hl7.Fhir.Model;

namespace Microsoft.Health.Fhir.Core.Messages.Get
{
    public class GetResourceResponse
    {
        public GetResourceResponse(Resource resource)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));

            Resource = resource;
        }

        public Resource Resource { get; }
    }
}
