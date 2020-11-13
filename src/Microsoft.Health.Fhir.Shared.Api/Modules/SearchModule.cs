// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Api.Features.Filters;
using Microsoft.Health.Fhir.Api.Features.Routing;
using Microsoft.Health.Fhir.Core.Features.Compartment;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Converters;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions.Parsers;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Shared.Core.Features.Search.Parameters;

namespace Microsoft.Health.Fhir.Api.Modules
{
    /// <summary>
    /// Registration of search components.
    /// </summary>
    public class SearchModule : IStartupModule
    {
        private readonly FhirServerConfiguration _configuration;

        public SearchModule(FhirServerConfiguration configuration)
        {
            EnsureArg.IsNotNull(configuration, nameof(configuration));

            _configuration = configuration;
        }

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
                .AsService<ISearchParameterDefinitionManager>()
                .AsService<IHostedService>();

            services.Add<SearchableSearchParameterDefinitionManager>()
                .Singleton()
                .AsSelf()
                .AsDelegate<ISearchParameterDefinitionManager.SearchableSearchParameterDefinitionManagerResolver>();

            services.Add<SupportedSearchParameterDefinitionManager>()
                .Singleton()
                .AsSelf()
                .AsService<ISupportedSearchParameterDefinitionManager>();

            services.Add<SearchParameterStatusManager>()
                .Singleton()
                .AsSelf()
                .AsImplementedInterfaces();

            services.Add<FilebasedSearchParameterStatusDataStore>()
                .Transient()
                .AsSelf()
                .AsService<ISearchParameterStatusDataStore>()
                .AsDelegate<FilebasedSearchParameterStatusDataStore.Resolver>();

            services.Add<SearchParameterSupportResolver>()
                .Singleton()
                .AsSelf()
                .AsImplementedInterfaces();

            // TypedElement based converters
            // These always need to be added as they are also used by the SearchParameterSupportResolver
            services.TypesInSameAssemblyAs<IFhirNodeToSearchValueTypeConverter>()
                .AssignableTo<IFhirNodeToSearchValueTypeConverter>()
                .Singleton()
                .AsService<IFhirNodeToSearchValueTypeConverter>();

            services.Add<FhirNodeToSearchValueTypeConverterManager>()
                .Singleton()
                .AsSelf()
                .AsService<IFhirNodeToSearchValueTypeConverterManager>();

            services.Add<CodeSystemResolver>()
                .Singleton()
                .AsSelf()
                .AsImplementedInterfaces();

            if (_configuration.CoreFeatures.UseTypedElementIndexer)
            {
                services.AddSingleton<ISearchIndexer, TypedElementSearchIndexer>();
            }
            else
            {
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
            }

            services.AddSingleton<ISearchParameterExpressionParser, SearchParameterExpressionParser>();
            services.AddSingleton<IExpressionParser, ExpressionParser>();
            services.AddSingleton<ISearchOptionsFactory, SearchOptionsFactory>();
            services.AddSingleton<IReferenceToElementResolver, LightweightReferenceToElementResolver>();

            services.Add<CompartmentDefinitionManager>()
                .Singleton()
                .AsSelf()
                .AsService<IHostedService>()
                .AsService<ICompartmentDefinitionManager>();

            services.Add<CompartmentIndexer>()
                .Singleton()
                .AsSelf()
                .AsService<ICompartmentIndexer>();

            services.AddSingleton<ISearchParameterValidator, SearchParameterValidator>();
            services.AddSingleton<ISearchParameterEditor, SearchParameterEditor>();
            services.AddSingleton<SearchParameterFilterAttribute>();
        }
    }
}
