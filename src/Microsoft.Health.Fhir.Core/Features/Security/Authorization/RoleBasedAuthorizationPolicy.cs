// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using EnsureThat;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;

namespace Microsoft.Health.Fhir.Core.Features.Security.Authorization
{
    public class RoleBasedAuthorizationPolicy : IAuthorizationPolicy
    {
        private readonly SecurityConfiguration _securityConfiguration;

        public RoleBasedAuthorizationPolicy(IOptions<SecurityConfiguration> securityConfiguration)
        {
            EnsureArg.IsNotNull(securityConfiguration?.Value, nameof(securityConfiguration));

            _securityConfiguration = securityConfiguration.Value;
        }

        public IEnumerable<ResourcePermission> GetApplicableResourcePermissions(ClaimsPrincipal user, ResourceAction action)
        {
            EnsureArg.IsNotNull(user, nameof(user));

            var roleClaims = GetRoleClaims(user);

            return _securityConfiguration.Authorization.Roles.Where(x => roleClaims.Contains(x.Name)).SelectMany(x => x.ResourcePermissions.Where(y => y.Actions.Contains(action)));
        }

        public bool HasActionPermission(ClaimsPrincipal user, ResourceAction action)
        {
            EnsureArg.IsNotNull(user, nameof(user));

            var roleClaims = GetRoleClaims(user);

            return _securityConfiguration.Authorization.Roles.Any(x => roleClaims.Contains(x.Name) && x.ResourcePermissions.Any(y => y.Actions.Contains(action)));
        }

        private static string[] GetRoleClaims(ClaimsPrincipal user)
        {
            return user.Claims.Where(claim => claim.Type == ClaimTypes.Role || claim.Type == AuthorizationConfiguration.RolesClaim).Select(x => x.Value).ToArray();
        }
    }
}
