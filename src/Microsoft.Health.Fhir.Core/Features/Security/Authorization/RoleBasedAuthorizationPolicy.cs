// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Configs;

namespace Microsoft.Health.Fhir.Core.Features.Security.Authorization
{
    public class RoleBasedAuthorizationPolicy : IAuthorizationPolicy
    {
        private readonly Dictionary<string, Role> _roles;
        private readonly Dictionary<string, IEnumerable<ResourceAction>> _roleNameToResourceActions;
        private readonly AuthorizationConfiguration _authorizationConfiguration;

        public RoleBasedAuthorizationPolicy(AuthorizationConfiguration authorizationConfiguration)
        {
            EnsureArg.IsNotNull(authorizationConfiguration, nameof(authorizationConfiguration));

            _authorizationConfiguration = authorizationConfiguration;
            _roles = authorizationConfiguration.Roles.ToDictionary(r => r.Name, StringComparer.InvariantCultureIgnoreCase);
            _roleNameToResourceActions = _roles.Select(kvp => KeyValuePair.Create(kvp.Key, kvp.Value.ResourcePermissions.Select(rp => rp.Actions).SelectMany(x => x))).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }

        public bool HasPermission(ClaimsPrincipal user, ResourceAction action)
        {
            EnsureArg.IsNotNull(user, nameof(user));
            IEnumerable<ResourceAction> actions = GetRolesAndActions(user);

            if (actions == null)
            {
                return false;
            }

            return actions.Contains(action);
        }

        private IEnumerable<ResourceAction> GetRolesAndActions(ClaimsPrincipal user)
        {
            var roles = user.Claims
                .Where(claim => claim.Type == _authorizationConfiguration.RolesClaim && _roles.ContainsKey(claim.Value))
                .Select(claim => _roles[claim.Value]);

            var actions = roles.Select(r => _roleNameToResourceActions[r.Name]).SelectMany(x => x).Distinct();

            return actions;
        }
    }
}
