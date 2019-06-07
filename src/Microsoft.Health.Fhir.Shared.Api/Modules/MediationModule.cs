// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using EnsureThat;
using MediatR;
using MediatR.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Persistence;

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

            services.AddMediatR(GetType().Assembly, KnownAssemblies.Core, KnownAssemblies.CoreVersionSpecific);
            services.AddTransient(typeof(IPipelineBehavior<,>), typeof(RequestPreProcessorBehavior<,>));
            services.AddTransient(typeof(IPipelineBehavior<,>), typeof(RequestPostProcessorBehavior<,>));

            Predicate<Type> isPipelineBehavior = y => y.IsGenericType && y.GetGenericTypeDefinition() == typeof(IPipelineBehavior<,>);

            services.TypesInSameAssembly(KnownAssemblies.Core, KnownAssemblies.CoreVersionSpecific)
                .Transient()
                .AsImplementedInterfaces(isPipelineBehavior);

            // Allows handlers to provide capabilities
            var openRequestInterfaces = new Type[]
            {
                typeof(IRequestHandler<,>),
                typeof(INotificationHandler<>),
            };

            services.TypesInSameAssembly(KnownAssemblies.Core, KnownAssemblies.CoreVersionSpecific)
                .Where(y => y.Type.IsGenericType && openRequestInterfaces.Contains(y.Type.GetGenericTypeDefinition()))
                .Transient()
                .AsImplementedInterfaces(x => x == typeof(IProvideCapability));
        }
    }
}
