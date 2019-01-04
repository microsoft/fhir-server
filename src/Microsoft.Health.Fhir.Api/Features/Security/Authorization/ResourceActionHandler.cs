// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
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
        private readonly Dictionary<string, ResourceAction> _resourceActionLookup = new Dictionary<string, ResourceAction>(StringComparer.OrdinalIgnoreCase);

        public ResourceActionHandler(IAuthorizationPolicy authorizationPolicy)
        {
            EnsureArg.IsNotNull(authorizationPolicy, nameof(authorizationPolicy));

            _authorizationPolicy = authorizationPolicy;

            foreach (ResourceAction resourceActionValue in Enum.GetValues(typeof(ResourceAction)))
            {
                _resourceActionLookup.Add(resourceActionValue.ToString(), resourceActionValue);
            }
        }

        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, ResourceActionRequirement requirement)
        {
            if (_resourceActionLookup.TryGetValue(requirement.PolicyName, out ResourceAction resourceAction) && _authorizationPolicy.HasPermission(context.User, resourceAction))
            {
                context.Succeed(requirement);
            }
            else
            {
                context.Fail();
            }

            return Task.CompletedTask;
        }
    }
}
