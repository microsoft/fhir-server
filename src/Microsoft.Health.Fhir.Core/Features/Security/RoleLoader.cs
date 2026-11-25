// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Hosting;
using Microsoft.Health.Fhir.Core.Configs;

namespace Microsoft.Health.Fhir.Core.Features.Security
{
    /// <summary>
    /// Loads the roles from <see cref="RoleCache"/> at startup and sets <see cref="AuthorizationConfiguration.Roles"/>.
    /// Invalid roles fail startup.
    /// </summary>
    public class RoleLoader : IHostedService
    {
        private readonly AuthorizationConfiguration _authorizationConfiguration;
        private readonly RoleCache _roleCache;

        public RoleLoader(AuthorizationConfiguration authorizationConfiguration, RoleCache roleCache)
        {
            _authorizationConfiguration = EnsureArg.IsNotNull(authorizationConfiguration, nameof(authorizationConfiguration));
            _roleCache = EnsureArg.IsNotNull(roleCache, nameof(roleCache));
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            RoleSet roles = await _roleCache.GetAsync(cancellationToken);
            _authorizationConfiguration.Roles = roles.Roles;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
