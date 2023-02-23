// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
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
            string logStmt = "Inside SecurityModule" + DateTime.Now.ToShortDateString();
            Console.WriteLine(" 1 " + logStmt);

            services.AddSingleton<IBundleHttpContextAccessor, BundleHttpContextAccessor>();

            Console.WriteLine(logStmt + " 2 ");

            // Set the token handler to not do auto inbound mapping. (e.g. "roles" -> "http://schemas.microsoft.com/ws/2008/06/identity/claims/role")
            JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();

            Console.WriteLine(logStmt + " 3 " + _securityConfiguration);

            if (_securityConfiguration.Enabled)
            {
                Console.WriteLine(logStmt + " 4 ");
                _securityConfiguration.AddAuthenticationLibrary(services, _securityConfiguration);

                Console.WriteLine(logStmt + " 5 ");

                services.AddControllers(mvcOptions =>
                {
                    var policy = new AuthorizationPolicyBuilder()
                        .RequireAuthenticatedUser()
                        .Build();

                    mvcOptions.Filters.Add(new AuthorizeFilter(policy));
                });

                Console.WriteLine(logStmt + " 6 ");
                if (_securityConfiguration.Authorization.Enabled)
                {
                    Console.WriteLine(logStmt + " 7 ");
                    services.Add<RoleLoader>().Transient().AsImplementedInterfaces();
                    Console.WriteLine(logStmt + " 8 ");
                    services.AddSingleton(_securityConfiguration.Authorization);
                    Console.WriteLine(logStmt + " 9 ");

                    services.AddSingleton<IAuthorizationService<DataActions>, RoleBasedFhirAuthorizationService>();
                    Console.WriteLine(logStmt + " 10 ");
                }
                else
                {
                    Console.WriteLine(logStmt + " 11 ");
                    services.AddSingleton<IAuthorizationService<DataActions>>(DisabledFhirAuthorizationService.Instance);
                    Console.WriteLine(logStmt + " 12 ");
                }
            }
            else
            {
                Console.WriteLine(logStmt + " 13 ");
                services.AddSingleton<IAuthorizationService<DataActions>>(DisabledFhirAuthorizationService.Instance);
                Console.WriteLine(logStmt + " 14 ");
            }
        }
    }
}
