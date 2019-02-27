// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Health.ControlPlane.Core.Features.Exceptions;
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

        public async Task DeleteIdentityProviderAsync(string name, string eTag, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNullOrWhiteSpace(name, nameof(name));
            await _controlPlaneDataStore.DeleteIdentityProviderAsync(name, eTag, cancellationToken);
        }

        public async Task<Role> GetRoleAsync(string name, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNullOrWhiteSpace(name, nameof(name));
            return await _controlPlaneDataStore.GetRoleAsync(name, cancellationToken);
        }

        public async Task<IEnumerable<IdentityProvider>> GetAllIdentityProvidersAsync(CancellationToken cancellationToken)
        {
            return await _controlPlaneDataStore.GetAllIdentityProvidersAsync(cancellationToken);
        }

        public async Task<IdentityProvider> GetIdentityProviderAsync(string name, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNullOrWhiteSpace(name, nameof(name));
            return await _controlPlaneDataStore.GetIdentityProviderAsync(name, cancellationToken);
        }

        public async Task<UpsertResponse<IdentityProvider>> UpsertIdentityProviderAsync(IdentityProvider identityProvider, string eTag, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(identityProvider, nameof(identityProvider));
            var validationResults = new List<ValidationResult>();

            if (!Validator.TryValidateObject(identityProvider, new ValidationContext(identityProvider), validationResults))
            {
                throw new InvalidDefinitionException(
                    Resources.IdentityProviderDefinitionIsInvalid,
                    validationResults);
            }

            return await _controlPlaneDataStore.UpsertIdentityProviderAsync(identityProvider, eTag, cancellationToken);
        }

        public async Task<IEnumerable<Role>> GetAllRolesAsync(CancellationToken cancellationToken)
        {
            return await _controlPlaneDataStore.GetAllRolesAsync(cancellationToken);
        }

        public async Task<UpsertResponse<Role>> UpsertRoleAsync(Role role, string eTag, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(role, nameof(role));
            var validationResults = new List<ValidationResult>();

            if (!Validator.TryValidateObject(role, new ValidationContext(role), validationResults))
            {
                throw new InvalidDefinitionException(
                    Resources.RoleDefinitionIsInvalid,
                    validationResults);
            }

            return await _controlPlaneDataStore.UpsertRoleAsync(role, eTag, cancellationToken);
        }

        public async Task DeleteRoleAsync(string name, string eTag, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNullOrWhiteSpace(name, nameof(name));
            await _controlPlaneDataStore.DeleteRoleAsync(name, eTag, cancellationToken);
        }
    }
}
