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
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;

namespace Microsoft.Health.Fhir.Core.Features.Security
{
    public class AppSettingsSecurityReadOnlyDataStore : ISecurityDataStore
    {
        private readonly RoleConfiguration _roleConfiguration;

        public AppSettingsSecurityReadOnlyDataStore(RoleConfiguration roleConfiguration)
        {
            EnsureArg.IsNotNull(roleConfiguration, nameof(roleConfiguration));

            _roleConfiguration = roleConfiguration;
        }

        public Task<IEnumerable<Role>> GetAllRolesAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(_roleConfiguration.Roles);
        }

        public Task<Role> GetRoleAsync(string name, CancellationToken cancellationToken)
        {
            var role = _roleConfiguration.Roles.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.InvariantCultureIgnoreCase));

            if (role == null)
            {
                throw new InvalidSearchOperationException("Role specified wasn't found");
            }

            return Task.FromResult(role);
        }

        public Task<Role> UpsertRoleAsync(Role role, WeakETag weakETag, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task DeleteRoleAsync(string name, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }
}
