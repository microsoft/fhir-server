// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Api.Features.Routing;
using Microsoft.Health.Fhir.Core.Features.Compartment;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Converters;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions.Parsers;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;

namespace Microsoft.Health.Fhir.Api.Modules
{
    /// <summary>
    /// Registration of search components.
    /// </summary>
    public class SearchModule : IStartupModule
    {
        /// <inheritdoc />
        public void Load(IServiceCollection services)
        {
            EnsureArg.IsNotNull(services, nameof(services));

            services.AddSingleton<IUrlResolver, UrlResolver>();
            services.AddSingleton<IBundleFactory, BundleFactory>();

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
            services.AddSingleton<ISearchParameterExpressionParser, SearchParameterExpressionParser>();
            services.AddSingleton<IExpressionParser, ExpressionParser>();
            services.AddSingleton<ISearchOptionsFactory, SearchOptionsFactory>();

            services.Add<CompartmentDefinitionManager>()
                .Singleton()
                .AsSelf()
                .AsService<IStartable>()
                .AsService<ICompartmentDefinitionManager>();

            services.Add<CompartmentIndexer>()
                .Singleton()
                .AsSelf()
                .AsService<ICompartmentIndexer>();

            // TODO: Remove the following once bug 65143 is fixed.
            // All of the classes that implement IProvideCapability will be automatically be picked up and registered.
            // This means that even though ResourceTypeManifestManager is not being registered, the service will still
            // try to instantiate but will fail since the dependency components are not registered. We should re-look
            // at the logic for automatically registering types since different component could have different life time.
            // For now, just manually remove the registration.
            RemoveRegistration(typeof(IProvideCapability), typeof(SearchParameterDefinitionManager), ServiceLifetime.Transient);

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
