// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.IO;
using System.Reflection;
using EnsureThat;
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
        /// Adds the sample authorization json file that is part of this project.
        /// </summary>
        /// <param name="services">The services collection.</param>
        /// <returns>IServiceCollection</returns>
        public static IServiceCollection AddSampleFhirAuthorization(this IServiceCollection services)
        {
            EnsureArg.IsNotNull(services, nameof(services));

            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("sampleauth.json"))
            using (TextReader reader = new StreamReader(stream))
            {
                RoleConfiguration roleConfiguration;

                roleConfiguration = RoleConfiguration.ValidateAndGetRoleConfiguration(reader.ReadToEnd());
                services.AddSingleton(Options.Create(roleConfiguration));
                services.Replace(ServiceDescriptor.Singleton<ISecurityDataStore, AppSettingsSecurityReadOnlyDataStore>());
            }

            return services;
        }

        /// <summary>
        /// Adds authorization from a stream pointing to a json authorization.
        /// </summary>
        /// <param name="services">The Services Collection.</param>
        /// <param name="stream">The stream pointing to authorization in json.</param>
        /// <returns>IServiceCollection</returns>
        public static IServiceCollection AddFhirAuthorization(this IServiceCollection services, Stream stream)
        {
            EnsureArg.IsNotNull(services, nameof(services));
            EnsureArg.IsNotNull(stream, nameof(stream));
            RoleConfiguration roleConfiguration;

            using (TextReader reader = new StreamReader(stream))
            {
                roleConfiguration = RoleConfiguration.ValidateAndGetRoleConfiguration(reader.ReadToEnd());
                services.AddSingleton(Options.Create(roleConfiguration));
                services.Replace(ServiceDescriptor.Singleton<ISecurityDataStore, AppSettingsSecurityReadOnlyDataStore>());
            }

            return services;
        }
    }
}
