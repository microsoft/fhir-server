// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;

namespace Microsoft.Health.Fhir.Api.Features.Security.Authorization
{
    internal class ResourceActionHandler : AuthorizationHandler<ResourceActionRequirement>
    {
        private readonly IAuthorizationPolicy _authorizationPolicy;

        public ResourceActionHandler(IAuthorizationPolicy authorizationPolicy)
        {
            EnsureArg.IsNotNull(authorizationPolicy, nameof(authorizationPolicy));
            _authorizationPolicy = authorizationPolicy;
        }

        protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, ResourceActionRequirement requirement)
        {
            if (Enum.TryParse<ResourceAction>(requirement.PolicyName, out var resourceAction) && await _authorizationPolicy.HasPermissionAsync(context.User, resourceAction))
            {
                context.Succeed(requirement);
            }
        }
    }
}
