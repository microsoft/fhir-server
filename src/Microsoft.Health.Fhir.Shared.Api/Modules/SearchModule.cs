// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
using EnsureThat;
using MediatR;
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
using Microsoft.Health.Fhir.Core.Features.Search.Filters;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Messages.Create;
using Microsoft.Health.Fhir.Core.Messages.Delete;
using Microsoft.Health.Fhir.Core.Messages.Search;
using Microsoft.Health.Fhir.Core.Messages.Storage;
using Microsoft.Health.Fhir.Core.Messages.Upsert;
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
                .AsService<IHostedService>()
                .ReplaceService<INotificationHandler<SearchParametersUpdatedNotification>>()
                .ReplaceService<INotificationHandler<StorageInitializedNotification>>();

            services.Add<SearchableSearchParameterDefinitionManager>()
                .Singleton()
                .AsSelf()
                .AsDelegate<ISearchParameterDefinitionManager.SearchableSearchParameterDefinitionManagerResolver>();

            services.Add<SupportedSearchParameterDefinitionManager>()
                .Singleton()
                .AsSelf()
                .AsService<ISupportedSearchParameterDefinitionManager>();

            services.Add<FilebasedSearchParameterStatusDataStore>()
                .Transient()
                .AsSelf()
                .AsService<ISearchParameterStatusDataStore>()
                .AsDelegate<FilebasedSearchParameterStatusDataStore.Resolver>();

            services.Add<SearchParameterStatusManager>()
                .Singleton()
                .AsSelf()
                .ReplaceService<INotificationHandler<SearchParameterDefinitionManagerInitialized>>();

            services.Add<SearchParameterSupportResolver>()
                .Singleton()
                .AsSelf()
                .AsImplementedInterfaces();

            // TypedElement based converters
            // These always need to be added as they are also used by the SearchParameterSupportResolver
            // Exclude the extension converter, since it will be dynamically created by the converter manager
            services.TypesInSameAssemblyAs<ITypedElementToSearchValueConverter>()
                .AssignableTo<ITypedElementToSearchValueConverter>()
                .Where(t => t.Type != typeof(FhirTypedElementToSearchValueConverterManager.ExtensionConverter))
                .Singleton()
                .AsService<ITypedElementToSearchValueConverter>();

            services.Add<FhirTypedElementToSearchValueConverterManager>()
                .Singleton()
                .AsSelf()
                .AsService<ITypedElementToSearchValueConverterManager>();

            services.Add<CodeSystemResolver>()
                .Singleton()
                .AsSelf()
                .AsImplementedInterfaces();

            services.AddSingleton<ISearchIndexer, TypedElementSearchIndexer>();

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
            services.AddSingleton<SearchParameterFilterAttribute>();
            services.AddSingleton<ISearchParameterOperations, SearchParameterOperations>();

            services.AddTransient<MissingDataFilterCriteria>();
            services.AddTransient<ISearchResultFilter, SearchResultFilter>();
            services.AddTransient(typeof(IPipelineBehavior<CreateResourceRequest, UpsertResourceResponse>), typeof(CreateOrUpdateSearchParameterBehavior<CreateResourceRequest, UpsertResourceResponse>));
            services.AddTransient(typeof(IPipelineBehavior<UpsertResourceRequest, UpsertResourceResponse>), typeof(CreateOrUpdateSearchParameterBehavior<UpsertResourceRequest, UpsertResourceResponse>));
            services.AddTransient(typeof(IPipelineBehavior<DeleteResourceRequest, DeleteResourceResponse>), typeof(DeleteSearchParameterBehavior<DeleteResourceRequest, DeleteResourceResponse>));
        }
    }
}
