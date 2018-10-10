// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;

namespace Microsoft.Health.Fhir.Core.Features.Security.Authorization
{
    public class AuthorizationPolicyClient : IAuthorizationPolicy
    {
        private readonly RoleConfiguration _roleConfiguration;
        private readonly Dictionary<string, Role> _roles;
        private readonly Dictionary<string, IEnumerable<ResourceAction>> _roleNameToResourceActions;

        public AuthorizationPolicyClient(IOptions<RoleConfiguration> roleConfiguration)
        {
            EnsureArg.IsNotNull(roleConfiguration?.Value, nameof(roleConfiguration));
            _roleConfiguration = roleConfiguration.Value;
            _roles = _roleConfiguration.Roles.ToDictionary(r => r.Name, StringComparer.InvariantCultureIgnoreCase);
            _roleNameToResourceActions = _roles.Select(kvp => KeyValuePair.Create(kvp.Key, kvp.Value.ResourcePermissions.Select(rp => rp.Actions).SelectMany(x => x))).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }

        public async Task<bool> HasPermissionAsync(ClaimsPrincipal user, ResourceAction action)
        {
            EnsureArg.IsNotNull(user, nameof(user));
            (IEnumerable<Role> roles, IEnumerable<ResourceAction> actions) = await GetRolesAndActions(user);

            if (actions == null)
            {
                return false;
            }

            return actions.Contains(action);
        }

        private async Task<(IEnumerable<Role>, IEnumerable<ResourceAction>)> GetRolesAndActions(ClaimsPrincipal user)
        {
            var roles = user.Claims
                .Where(claim => claim.Type == ClaimTypes.Role && _roles.ContainsKey(claim.Value))
                .Select(claim => _roles[claim.Value]);

            var actions = roles?.Select(r => _roleNameToResourceActions[r.Name]).SelectMany(x => x);

            return await Task.FromResult((roles, actions));
        }
    }
}
