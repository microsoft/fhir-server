// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;

namespace Microsoft.Health.Fhir.Core.Features.Security
{
    public class AppSettingsSecurityReadOnlyDataStore : ISecurityDataStore
    {
        private readonly Dictionary<string, Role> _roleLookup = new Dictionary<string, Role>(StringComparer.InvariantCultureIgnoreCase);

        public AppSettingsSecurityReadOnlyDataStore(IOptions<RoleConfiguration> roleConfiguration)
        {
            EnsureArg.IsNotNull(roleConfiguration?.Value, nameof(roleConfiguration));

            _roleLookup = roleConfiguration.Value.Roles.ToDictionary(r => r.Name);
        }

        public Task<IReadOnlyList<Role>> GetAllRolesAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult((IReadOnlyList<Role>)_roleLookup.Values.ToList());
        }

        public Task<Role> GetRoleAsync(string name, CancellationToken cancellationToken)
        {
            if (!_roleLookup.ContainsKey(name))
            {
                throw new KeyNotFoundException(Core.Resources.RoleNotFound);
            }

            return Task.FromResult(_roleLookup[name]);
        }
    }
}
