// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Api.Features.Routing;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Converters;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions.Parsers;
using Microsoft.Health.Fhir.Core.Features.Search.Legacy;
using Microsoft.Health.Fhir.Core.Features.Search.Legacy.Expressions;
using Microsoft.Health.Fhir.Core.Features.Search.Legacy.SearchValues;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;

namespace Microsoft.Health.Fhir.Api.Modules
{
    /// <summary>
    /// Registration of search components.
    /// </summary>
    public class SearchModule : IStartupModule
    {
        private readonly IConfiguration _configuration;

        public SearchModule(IConfiguration configuration)
        {
            EnsureArg.IsNotNull(configuration, nameof(configuration));

            _configuration = configuration;
        }

        /// <inheritdoc />
        public void Load(IServiceCollection services)
        {
            EnsureArg.IsNotNull(services, nameof(services));

            SearchConfiguration searchConfiguration = new SearchConfiguration();

            _configuration.GetSection("Search").Bind(searchConfiguration);

            services.AddSingleton(Options.Create(searchConfiguration));

            services.AddSingleton<IUrlResolver, UrlResolver>();
            services.AddSingleton<IBundleFactory, BundleFactory>();

            if (searchConfiguration.UseLegacySearch)
            {
                services.AddSingleton<ISearchParamDefinitionManager, SearchParamDefinitionManager>();
                services.AddSingleton<ISearchParamFactory, SearchParamFactory>();
                services.Add<ResourceTypeManifestManager>()
                    .Singleton()
                    .AsSelf()
                    .AsService<IResourceTypeManifestManager>()
                    .AsService<IProvideCapability>();
                services.AddSingleton<ISearchIndexer, LegacySearchIndexer>();
                services.AddSingleton<ILegacySearchValueParser, LegacySearchValueParser>();
                services.AddTransient<ILegacySearchValueExpressionBuilder, LegacySearchValueExpressionBuilder>();
                services.AddSingleton<ILegacyExpressionParser, LegacyExpressionParser>();
                services.AddSingleton<ISearchOptionsFactory, LegacySearchOptionsFactory>();
            }
            else
            {
                services.AddSingleton<IReferenceSearchValueParser, ReferenceSearchValueParser>();

                services.Add<SearchParameterDefinitionManager>()
                    .Singleton()
                    .AsSelf()
                    .AsService<IStartable>()
                    .AsService<IProvideCapability>()
                    .AsService<ISearchParameterDefinitionManager>();

                services.TypesInSameAssemblyAs<IFhirElementToSearchValueTypeConverter>()
                    .AssignableTo<IFhirElementToSearchValueTypeConverter>()
                    .Singleton()
                    .AsSelf()
                    .AsService<IFhirElementToSearchValueTypeConverter>();

                services.Add<FhirElementToSearchValueTypeConverterManager>()
                    .Singleton()
                    .AsSelf()
                    .AsService<IFhirElementToSearchValueTypeConverterManager>();

                services.AddSingleton<ISearchIndexer, SearchIndexer>();
                services.AddTransient<ISearchValueExpressionBuilder, SearchValueExpressionBuilder>();
                services.AddSingleton<IExpressionParser, ExpressionParser>();
                services.AddSingleton<ISearchOptionsFactory, SearchOptionsFactory>();
            }

            // TODO: Remove the following once bug 65143 is fixed.
            // All of the classes that implement IProvideCapability will be automatically be picked up and registered.
            // This means that even though ResourceTypeManifestManager is not being registered, the service will still
            // try to instantiate but will fail since the dependency components are not regsitered. We should re-look
            // at the logic for automatically registering types since different component could have different life time.
            // For now, just manually remove the registration.
            RemoveRegistration(typeof(IProvideCapability), typeof(SearchParameterDefinitionManager), ServiceLifetime.Transient);
            RemoveRegistration(typeof(IProvideCapability), typeof(ResourceTypeManifestManager), ServiceLifetime.Transient);

            void RemoveRegistration(Type serviceType, Type implementationType, ServiceLifetime lifetime)
            {
                for (int i = 0; i < services.Count; i++)
                {
                    ServiceDescriptor descriptor = services[i];

                    if (descriptor.ServiceType == serviceType &&
                        descriptor.ImplementationType == implementationType &&
                        descriptor.Lifetime == lifetime)
                    {
                        services.RemoveAt(i);
                        break;
                    }
                }
            }
        }
    }
}
