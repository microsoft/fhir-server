// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Api.Features.Security;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Web.Extensions;

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

            SecurityConfiguration securityConfiguration = new SecurityConfiguration();
            _configuration.GetSection("Security").Bind(securityConfiguration);

            // Adds the sample authorization present as part of the project to the dependency chain
            // Change to use service.AddFhirAuthorization with a stream pointing to a json file for a custom auth specification.
            if (securityConfiguration.EnableAuthorization)
            {
                services.AddSampleFhirAuthorization();
            }

            // You can create your own FhirAccessRequirementHandler by implementating an IAuthorizationHandler.
            // Replace the DefaultFhirAccessRequirementHandler here with your custom implementation to have it handle the FhirAccessRequirement.
            services.AddSingleton<IAuthorizationHandler, DefaultFhirAccessRequirementHandler>();
        }
    }
}
