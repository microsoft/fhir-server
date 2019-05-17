// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Messages.Get
{
    public class GetSystemCapabilitiesResponse
    {
        public GetSystemCapabilitiesResponse(ResourceElement capabilityStatement)
        {
            EnsureArg.IsNotNull(capabilityStatement, nameof(capabilityStatement));

            CapabilityStatement = capabilityStatement;
        }

        public ResourceElement CapabilityStatement { get; }
    }
}
