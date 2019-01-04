// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using EnsureThat;
using Microsoft.Extensions.Options;
using Microsoft.Health.Abstractions.Cryptography;
using Microsoft.Health.ControlPlane.Core.Configs;
using Microsoft.Health.Extensions.DependencyInjection;
using Newtonsoft.Json;

namespace Microsoft.Health.ControlPlane.Core.Features.Rbac
{
    public class RbacServiceBootstrapper : IStartable
    {
        private readonly Func<IScoped<IRbacService>> _rbacServiceFactory;
        private readonly ControlPlaneConfiguration _controlPlaneConfiguration;

        public RbacServiceBootstrapper(Func<IScoped<IRbacService>> rbacServiceFactory, IOptions<ControlPlaneConfiguration> controlPlaneConfiguration)
        {
            EnsureArg.IsNotNull(rbacServiceFactory, nameof(rbacServiceFactory));
            EnsureArg.IsNotNull(controlPlaneConfiguration?.Value, nameof(controlPlaneConfiguration));

            _rbacServiceFactory = rbacServiceFactory;
            _controlPlaneConfiguration = controlPlaneConfiguration.Value;
        }

        public async void Start()
        {
            using (var scopedRbacService = _rbacServiceFactory())
            {
                IRbacService rbacService = scopedRbacService.Value;

                var bootstrapHash = Hasher.ComputeHash(JsonConvert.SerializeObject(_controlPlaneConfiguration.Bootstrap));

                if (!await rbacService.IsBootstrappedAsync(bootstrapHash, CancellationToken.None) && _controlPlaneConfiguration.Bootstrap != null)
                {
                    foreach (var idp in _controlPlaneConfiguration.Bootstrap.IdentityProviders)
                    {
                        await rbacService.UpsertIdentityProviderAsync(idp, CancellationToken.None);
                    }

                    // TODO: Persist roles and role memberships while bootstrapping
                }
            }
        }
    }
}
