// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.IdentityModel.Tokens.Jwt;
using EnsureThat;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Api.Features.Bundle;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;

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

            services.AddSingleton<IBundleHttpContextAccessor, BundleHttpContextAccessor>();

            // Set the token handler to not do auto inbound mapping. (e.g. "roles" -> "http://schemas.microsoft.com/ws/2008/06/identity/claims/role")
            JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();

            if (_securityConfiguration.Enabled)
            {
                _securityConfiguration.AddAuthenticationLibrary(services, _securityConfiguration);

                services.AddAuthorization(options =>
                {
                    var policy = new AuthorizationPolicyBuilder()
                        .RequireAuthenticatedUser()
                        .Build();
                });

                if (_securityConfiguration.Authorization.Enabled)
                {
                    services.Add<RoleLoader>().Transient().AsImplementedInterfaces();
                    services.AddSingleton(_securityConfiguration.Authorization);

                    services.AddSingleton<IAuthorizationService<DataActions>, RoleBasedFhirAuthorizationService>();
                }
                else
                {
                    services.AddSingleton<IAuthorizationService<DataActions>>(DisabledFhirAuthorizationService.Instance);
                }
            }
            else
            {
                services.AddSingleton<IAuthorizationService<DataActions>>(DisabledFhirAuthorizationService.Instance);
            }
        }
    }
}
