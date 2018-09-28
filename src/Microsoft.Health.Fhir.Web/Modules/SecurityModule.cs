// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Api.Features.Security;
using Microsoft.Health.Fhir.Core.Configs;

namespace Microsoft.Health.Fhir.Web.Modules
{
    public class SecurityModule : IStartupModule
    {
        private readonly IConfiguration _configuration;

        public SecurityModule(IConfiguration configuration)
        {
            EnsureArg.IsNotNull(configuration, nameof(configuration));

            _configuration = configuration;
        }

        public void Load(IServiceCollection services)
        {
            EnsureArg.IsNotNull(services, nameof(services));

            AuthorizationConfiguration authorizationConfiguration = new AuthorizationConfiguration();
            IConfigurationSection authSection = _configuration.GetSection("Security:Authorization");
            authSection.Bind(authorizationConfiguration);

            if (authorizationConfiguration.Enabled)
            {
                var roleConfiguration = new RoleConfiguration();
                authSection.Bind(roleConfiguration);
                roleConfiguration.Validate();
                authorizationConfiguration.RoleConfiguration = roleConfiguration;

                // Adds the authorization specification as part of the appsettings to the dependency chain
                // Change appsettings for a custom auth specification.
                services.AddSingleton(Options.Create(authorizationConfiguration));
            }

            // You can create your own FhirAccessRequirementHandler by implementating an IAuthorizationHandler.
            // Replace the DefaultFhirAccessRequirementHandler here with your custom implementation to have it handle the FhirAccessRequirement.
            services.AddSingleton<IAuthorizationHandler, DefaultFhirAccessRequirementHandler>();
        }
    }
}
