// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

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

        public void Load(IServiceCollection services)
        {
            EnsureArg.IsNotNull(services, nameof(services));

            CorsPolicy corsPolicy;

            switch (_corsConfiguration.Mode)
            {
                case CorsMode.All:
                    corsPolicy = new CorsPolicyBuilder()
                        .AllowAnyOrigin()
                        .AllowAnyHeader()
                        .AllowAnyMethod()
                        .Build();
                    break;
                case CorsMode.Custom:
                    corsPolicy = new CorsPolicyBuilder()
                        .WithOrigins(_corsConfiguration.AllowedOrigins.ToArray())
                        .AllowAnyHeader()
                        .AllowAnyMethod()
                        .Build();
                    break;
                default:
                    corsPolicy = new CorsPolicy();
                    break;
            }

            services.AddCors(options =>
            {
                options.AddPolicy(
                    Constants.DefaultCorsPolicy,
                    corsPolicy);
            });
        }
    }
}
