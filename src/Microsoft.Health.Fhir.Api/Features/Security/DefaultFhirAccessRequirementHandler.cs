// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;

namespace Microsoft.Health.Fhir.Api.Features.Security
{
    public class DefaultFhirAccessRequirementHandler : AuthorizationHandler<FhirAccessRequirement>
    {
        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, FhirAccessRequirement requirement)
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }
    }
}
