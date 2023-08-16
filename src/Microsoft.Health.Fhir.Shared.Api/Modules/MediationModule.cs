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

            services.AddMediatR(cfg =>
            {
                cfg.RegisterServicesFromAssemblies(KnownAssemblies.All);
                cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(RequestExceptionActionProcessorBehavior<,>));
                cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(RequestExceptionProcessorBehavior<,>));
                cfg.AddRequestPreProcessor(typeof(IRequestPreProcessor<>), typeof(ValidateCapabilityPreProcessor<>));
                cfg.AddRequestPreProcessor(typeof(IRequestPreProcessor<>), typeof(ValidateRequestPreProcessor<>));
                cfg.AddRequestPreProcessor(typeof(IRequestPreProcessor<BundleRequest>), typeof(ValidateBundlePreProcessor));
        });

/*            services.AddMediatR(cfg =>
                cfg.RegisterServicesFromAssemblies(KnownAssemblies.All)
                    .AddBehavior(typeof(IPipelineBehavior<,>), typeof(RequestExceptionActionProcessorBehavior<,>))
                    .AddBehavior(typeof(IPipelineBehavior<,>), typeof(RequestExceptionProcessorBehavior<,>))
                    .AddBehavior(typeof(IPipelineBehavior<,>), typeof(RequestPreProcessorBehavior<,>))
                    .AddBehavior(typeof(IPipelineBehavior<,>), typeof(RequestPostProcessorBehavior<,>)));*/

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
