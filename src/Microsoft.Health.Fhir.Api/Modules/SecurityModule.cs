// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Api.Features.Security;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Security;

namespace Microsoft.Health.Fhir.Api.Modules
{
    public class SecurityModule : IStartupModule
    {
        private readonly IConfiguration _configuration;

        public SecurityModule(IConfiguration configuration)
        {
            EnsureArg.IsNotNull(configuration, nameof(configuration));

            _configuration = configuration;
        }

        /// <inheritdoc />
        public void Load(IServiceCollection services)
        {
            EnsureArg.IsNotNull(services, nameof(services));

            SecurityConfiguration securityConfiguration = new SecurityConfiguration();
            _configuration.GetSection("Security").Bind(securityConfiguration);
            services.AddSingleton(Options.Create(securityConfiguration));

            if (securityConfiguration.Enabled && securityConfiguration.Authentication?.Mode == AuthenticationMode.Jwt)
            {
                services.AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
                        options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
                    })
                    .AddJwtBearer(options =>
                    {
                        options.Authority = securityConfiguration.Authentication.Authority;
                        options.Audience = securityConfiguration.Authentication.Audience;
                        options.RequireHttpsMetadata = true;
                    });

                services.AddAuthorization(options => options.AddPolicy(PolicyNames.FhirPolicy, builder =>
                {
                    builder.RequireAuthenticatedUser();
                    builder.Requirements.Add(new FhirAccessRequirement());
                }));
            }
            else
            {
                services.AddAuthorization(options =>
                    options.AddPolicy(PolicyNames.FhirPolicy, builder => builder.RequireAssertion(x => true)));
            }
        }
    }
}
