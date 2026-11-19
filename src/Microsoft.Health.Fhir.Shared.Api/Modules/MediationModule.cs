// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using EnsureThat;
using Medino;
using Medino.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Api.Features.Resources;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Search.Behavior;
using Microsoft.Health.Fhir.Core.Features.Validation;
using Microsoft.Health.Fhir.Core.Messages.Bundle;

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

            services.AddMedino(cfg =>
            {
               cfg.RegisterServicesFromAssemblies(KnownAssemblies.All);
            });

            RemovePipelineBehaviorRegistrations<ProvenanceHeaderBehavior>(services);
            RemovePipelineBehaviorRegistrations<ProfileResourcesBehaviour>(services);
            RemovePipelineBehaviorRegistrations<ListSearchPipeBehavior>(services);

            services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidateRequestPreProcessor<,>));
            services.RemoveAll<IPipelineBehavior<BundleRequest, BundleResponse>>();
            services.AddTransient<IPipelineBehavior<BundleRequest, BundleResponse>, ValidateBundlePreProcessor>();
            services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidateCapabilityPreProcessor<,>));

            // Allows handlers to provide capabilities
            var openRequestInterfaces = new[]
            {
                typeof(IRequestHandler<,>),
                typeof(INotificationHandler<>),
            };

            services.TypesInSameAssembly(KnownAssemblies.All)
                .Where(y => y.Type.IsGenericType && openRequestInterfaces.Contains(y.Type.GetGenericTypeDefinition()))
                .Transient()
                .AsImplementedInterfaces(x => x == typeof(IProvideCapability));
        }

        private static void RemovePipelineBehaviorRegistrations<TBehavior>(IServiceCollection services)
        {
            Type behaviorType = typeof(TBehavior);

            foreach (Type serviceType in behaviorType.GetInterfaces().Where(IsPipelineBehavior))
            {
                for (int i = services.Count - 1; i >= 0; i--)
                {
                    ServiceDescriptor descriptor = services[i];
                    if (descriptor.ServiceType == serviceType && descriptor.ImplementationType == behaviorType)
                    {
                        services.RemoveAt(i);
                    }
                }
            }
        }

        private static bool IsPipelineBehavior(Type type)
        {
            return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IPipelineBehavior<,>);
        }
    }
}
