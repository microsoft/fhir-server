// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Api.Features.Security;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Security;

namespace Microsoft.Health.Fhir.Api.Modules
{
    public class SecurityModule : IStartupModule
    {
        private readonly SecurityConfiguration _securityConfiguration;

        public SecurityModule(FhirServerConfiguration fhirServerConfiguration)
        {
            EnsureArg.IsNotNull(fhirServerConfiguration, nameof(fhirServerConfiguration));
            _securityConfiguration = fhirServerConfiguration.Security;
        }

        /// <inheritdoc />
        public void Load(IServiceCollection services)
        {
            EnsureArg.IsNotNull(services, nameof(services));

            if (_securityConfiguration.Enabled && _securityConfiguration.Authentication?.Mode == AuthenticationMode.Jwt)
            {
                services.AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
                        options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
                    })
                    .AddJwtBearer(options =>
                    {
                        options.Authority = _securityConfiguration.Authentication.Authority;
                        options.Audience = _securityConfiguration.Authentication.Audience;
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
