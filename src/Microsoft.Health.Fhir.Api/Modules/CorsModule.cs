// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using EnsureThat;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Cors;

namespace Microsoft.Health.Fhir.Api.Modules
{
    public class CorsModule : IStartupModule
    {
        private readonly CorsConfiguration _corsConfiguration;

        public CorsModule(FhirServerConfiguration fhirServerConfiguration)
        {
            EnsureArg.IsNotNull(fhirServerConfiguration, nameof(fhirServerConfiguration));
            _corsConfiguration = fhirServerConfiguration.Cors;
        }

        internal CorsPolicy DefaultCorsPolicy { get; private set; }

        public void Load(IServiceCollection services)
        {
            EnsureArg.IsNotNull(services, nameof(services));

            var corsPolicyBuilder = new CorsPolicyBuilder()
                .WithOrigins(_corsConfiguration.Origins.ToArray())
                .WithHeaders(_corsConfiguration.Headers.ToArray())
                .WithMethods(_corsConfiguration.Methods.ToArray());

            if (_corsConfiguration.MaxAge != null)
            {
                corsPolicyBuilder.SetPreflightMaxAge(TimeSpan.FromSeconds(_corsConfiguration.MaxAge.Value));
            }

            if (_corsConfiguration.AllowCredentials)
            {
                corsPolicyBuilder.AllowCredentials();
            }

            DefaultCorsPolicy = corsPolicyBuilder.Build();

            services.AddCors(options =>
            {
                options.AddPolicy(
                    Constants.DefaultCorsPolicy,
                    DefaultCorsPolicy);
            });
        }
    }
}
