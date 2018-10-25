// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.AspNetCore.Authorization;

namespace Microsoft.Health.Fhir.Api.Features.Security.Authorization
{
    internal class ResourceActionRequirement : IAuthorizationRequirement
    {
        public ResourceActionRequirement(string policyName)
        {
            PolicyName = policyName;
        }

        public string PolicyName { get; }
    }
}
