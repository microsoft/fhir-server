// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Health.ControlPlane.Core.Features.Persistence;

namespace Microsoft.Health.ControlPlane.Core.Features.Rbac
{
    public class RbacService : IRbacService
    {
        private readonly IControlPlaneDataStore _controlPlaneDataStore;

        public RbacService(IControlPlaneDataStore controlPlaneDataStore)
        {
            EnsureArg.IsNotNull(controlPlaneDataStore, nameof(controlPlaneDataStore));

            _controlPlaneDataStore = controlPlaneDataStore;
        }

        public async Task<IdentityProvider> GetIdentityProviderAsync(string name, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNullOrWhiteSpace(name, nameof(name));

            return await _controlPlaneDataStore.GetIdentityProviderAsync(name, cancellationToken);
        }

        public async Task<IdentityProvider> UpsertIdentityProviderAsync(IdentityProvider identityProvider, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(identityProvider, nameof(identityProvider));

            return await _controlPlaneDataStore.UpsertIdentityProviderAsync(identityProvider, cancellationToken);
        }
    }
}
