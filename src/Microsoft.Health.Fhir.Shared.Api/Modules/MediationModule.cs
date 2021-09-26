// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Reflection;
using EnsureThat;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Conformance;

namespace Microsoft.Health.Fhir.Api.Modules
{
    /// <summary>
    /// Installs mediation components in container
    /// </summary>
    public class MediationModule : IStartupModule
    {
        /// <inheritdoc />
        public void Load(IServiceCollection services)
        {
            EnsureArg.IsNotNull(services, nameof(services));

            RegisterAssemblies(services, KnownAssemblies.All);
        }

        internal static void RegisterAssemblies(IServiceCollection services, params Assembly[] assemblies)
        {
            EnsureArg.HasItems(assemblies, nameof(assemblies));

            services.AddMediatR(assemblies);

            // Allows handlers to provide capabilities
            var openRequestInterfaces = new[]
            {
                typeof(IRequestHandler<,>),
                typeof(INotificationHandler<>),
            };

            services.TypesInSameAssembly(assemblies)
                .Where(y => y.Type.IsGenericType && openRequestInterfaces.Contains(y.Type.GetGenericTypeDefinition()))
                .Transient()
                .AsImplementedInterfaces(x => x == typeof(IProvideCapability));
        }
    }
}
