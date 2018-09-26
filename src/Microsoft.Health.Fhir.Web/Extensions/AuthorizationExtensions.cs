// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Security;

namespace Microsoft.Health.Fhir.Web.Extensions
{
    public static class AuthorizationExtensions
    {
        /// <summary>
        /// Adds authorization from appsettings.
        /// </summary>
        /// <param name="services">The services collection.</param>
        /// <param name="authSection">ConfigurationSection for Authorization.</param>
        /// <returns>IServiceCollection</returns>
        public static IServiceCollection AddFhirAuthorization(this IServiceCollection services, IConfigurationSection authSection)
        {
            EnsureArg.IsNotNull(services, nameof(services));
            EnsureArg.IsNotNull(authSection, nameof(authSection));

            RoleConfiguration roleConfiguration = new RoleConfiguration();

            authSection.Bind(roleConfiguration);
            services.AddSingleton(Options.Create(roleConfiguration));
            services.Replace(ServiceDescriptor.Singleton<ISecurityDataStore, AppSettingsSecurityReadOnlyDataStore>());

            return services;
        }
    }
}
