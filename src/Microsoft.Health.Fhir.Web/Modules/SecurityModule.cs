// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Api.Features.Security;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Web.Configs;
using Microsoft.Health.Fhir.Web.Features.Filters;

namespace Microsoft.Health.Fhir.Web.Modules
{
    public class SecurityModule : IStartupModule
    {
        private readonly IConfiguration _configuration;
        private readonly WebFeatureConfiguration _webFeatureConfiguration = new WebFeatureConfiguration();

        public SecurityModule(IConfiguration configuration)
        {
            EnsureArg.IsNotNull(configuration, nameof(configuration));

            _configuration = configuration;
        }

        public void Load(IServiceCollection services)
        {
            EnsureArg.IsNotNull(services, nameof(services));

            _configuration.GetSection("Features").Bind(_webFeatureConfiguration);
            services.AddSingleton(Options.Create(_webFeatureConfiguration));

            services.AddSingleton<SecurityControllerFeatureFilterAttribute>();

            if (_webFeatureConfiguration.UseAppSettingsRoleStore)
            {
                var roleConfiguration = new RoleConfiguration();
                _configuration.GetSection("Security").Bind(roleConfiguration);
                services.AddSingleton(Options.Create(roleConfiguration));

                services.Replace(ServiceDescriptor.Singleton<ISecurityDataStore, AppSettingsSecurityDataStore>());
            }

            // You can create your own FhirAccessRequirementHandler by implementating an IAuthorizationHandler.
            // Replace the DefaultFhirAccessRequirementHandler here with your custom implementation to have it handle the FhirAccessRequirement.
            services.AddSingleton<IAuthorizationHandler, DefaultFhirAccessRequirementHandler>();

            services.AddScoped<ISecurityRepository, SecurityRepository>();
        }
    }
}
