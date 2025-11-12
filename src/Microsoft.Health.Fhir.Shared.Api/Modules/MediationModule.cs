// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using EnsureThat;
using Medino;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Conformance;
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

            // TODO: AddMedino extension method not available in Medino 3.0.2 - may need alternative registration
            // services.AddMedino(cfg =>
            // {
            //     cfg.RegisterServicesFromAssemblies(KnownAssemblies.All);
            //     cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(RequestExceptionActionProcessorBehavior<,>));
            //     cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(RequestExceptionProcessorBehavior<,>));
            // });

            // Register ValidateBundlePreProcessor as a pipeline behavior
            // (Converted from IRequestPreProcessor which was removed in newer Medino versions)
            services.AddTransient<IPipelineBehavior<BundleRequest, BundleResponse>, ValidateBundlePreProcessor>();

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
    }
}
