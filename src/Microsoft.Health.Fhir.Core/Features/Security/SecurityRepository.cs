// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Persistence;

namespace Microsoft.Health.Fhir.Core.Features.Security
{
    public class SecurityRepository : ISecurityRepository
    {
        private readonly ISecurityDataStore _dataStore;

        public SecurityRepository(
            ISecurityDataStore dataStore)
        {
            EnsureArg.IsNotNull(dataStore, nameof(dataStore));

            _dataStore = dataStore;
        }

        public async Task<IEnumerable<Role>> GetAllRolesAsync(CancellationToken cancellationToken)
        {
            return await _dataStore.GetAllRolesAsync(cancellationToken);
        }

        public async Task<Role> GetRoleAsync(string name, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNullOrWhiteSpace(name, nameof(name));

            return await _dataStore.GetRoleAsync(name, cancellationToken);
        }

        public async Task<Role> UpsertRoleAsync(Role role, WeakETag weakETag, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(role, nameof(role));

            return await _dataStore.UpsertRoleAsync(role, weakETag, cancellationToken);
        }

        public async Task DeleteRoleAsync(string name, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNullOrWhiteSpace(name, nameof(name));

            await _dataStore.DeleteRoleAsync(name, cancellationToken);
        }
    }
}
