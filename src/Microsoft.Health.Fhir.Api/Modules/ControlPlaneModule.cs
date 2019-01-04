// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.ControlPlane.Core.Features.Rbac;
using Microsoft.Health.Extensions.DependencyInjection;

namespace Microsoft.Health.Fhir.Api.Modules
{
    public class ControlPlaneModule : IStartupModule
    {
        public ControlPlaneModule()
        {
        }

        /// <inheritdoc />
        public void Load(IServiceCollection services)
        {
            EnsureArg.IsNotNull(services, nameof(services));

            services.Add<RbacService>()
                .Scoped()
                .AsService<IRbacService>();

            services.AddFactory<IScoped<IRbacService>>();

            services.Add<RbacServiceBootstrapper>()
                .Singleton()
                .AsService<IStartable>();
        }
    }
}
