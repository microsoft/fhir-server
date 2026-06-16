// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
using EnsureThat;
using Medino;
using Medino.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Validation;

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

            services.AddMedino(KnownAssemblies.All);

            // Medino has no IRequestPreProcessor. The two closed validation behaviors
            // (ValidateBundlePreProcessor) are auto-registered by AddMedino's assembly scan.
            // The open-generic validation behaviors are skipped by the scan and must be
            // registered manually as open-generic IPipelineBehavior<,>.
            services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidateRequestPreProcessor<,>));
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
    }
}
