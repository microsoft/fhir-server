// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Medino;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Api.Features.Filters;
using Microsoft.Health.Fhir.Api.Features.Routing;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Compartment;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Access;
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
using Microsoft.Health.Fhir.Core.Models;
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

            services
                .RemoveServiceTypeExact<SearchParameterDefinitionManager, INotificationHandler<SearchParametersUpdatedNotification>>()
                .RemoveServiceTypeExact<SearchParameterDefinitionManager, INotificationHandler<StorageInitializedNotification>>()
                .Add<SearchParameterDefinitionManager>()
                .Singleton()
                .AsSelf()
                .AsService<ISearchParameterDefinitionManager>()
                .AsService<INotificationHandler<SearchParametersUpdatedNotification>>()
                .AsService<INotificationHandler<StorageInitializedNotification>>();

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

            services
                .RemoveServiceTypeExact<SearchParameterStatusManager, INotificationHandler<SearchParameterDefinitionManagerInitialized>>()
                .Add<SearchParameterStatusManager>()
                .Singleton()
                .AsSelf()
                .AsService<ISearchParameterStatusManager>()
                .AsService<INotificationHandler<SearchParameterDefinitionManagerInitialized>>();

            // Register the SearchParameter cache refresh background service
            services
                .RemoveServiceTypeExact<SearchParameterCacheRefreshBackgroundService, INotificationHandler<SearchParametersInitializedNotification>>()
                .Add<SearchParameterCacheRefreshBackgroundService>()
                .Singleton()
                .AsSelf()
                .AsService<IHostedService>()
                .AsService<INotificationHandler<SearchParametersInitializedNotification>>();

            services.Add<SearchParameterSupportResolver>()
                .Singleton()
                .AsSelf()
                .AsImplementedInterfaces();

            var searchValueConverterExclusions = new HashSet<Type>
            {
                typeof(FhirTypedElementToSearchValueConverterManager.ExtensionConverter),
            };

#if STU3 || R4 || R4B
            // These converters are required only for R5.
            searchValueConverterExclusions.Add(typeof(CanonicalToReferenceSearchValueConverter));
            searchValueConverterExclusions.Add(typeof(IdentifierToStringSearchValueConverter));
            searchValueConverterExclusions.Add(typeof(IdToReferenceSearchValueConverter));
#endif

            // TypedElement based converters
            // These always need to be added as they are also used by the SearchParameterSupportResolver
            // Exclude the extension converter, since it will be dynamically created by the converter manager
            services.TypesInSameAssemblyAs<ITypedElementToSearchValueConverter>()
                .AssignableTo<ITypedElementToSearchValueConverter>()
                .Where(t => !searchValueConverterExclusions.Contains(t.Type))
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
            services.AddTransient<ExpressionAccessControl>();

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
            services.AddSingleton<ISearchParameterComparer<SearchParameterInfo>, SearchParameterComparer>();

            services.AddTransient<MissingDataFilterCriteria>();
            services.AddTransient<IDataResourceFilter, DataResourceFilter>();
            services.AddTransient<IPipelineBehavior<CreateResourceRequest, UpsertResourceResponse>, CreateOrUpdateSearchParameterBehavior<CreateResourceRequest, UpsertResourceResponse>>();
            services.AddTransient<IPipelineBehavior<UpsertResourceRequest, UpsertResourceResponse>, CreateOrUpdateSearchParameterBehavior<UpsertResourceRequest, UpsertResourceResponse>>();
            services.AddTransient<IPipelineBehavior<DeleteResourceRequest, DeleteResourceResponse>, DeleteSearchParameterBehavior<DeleteResourceRequest, DeleteResourceResponse>>();
        }
    }
}
