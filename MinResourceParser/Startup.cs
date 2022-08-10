// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Compartment;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Converters;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions.Parsers;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;

namespace MinResourceParser
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public virtual void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton<IReferenceSearchValueParser, ReferenceSearchValueParser>();

            /* Replace this one
            services.Add<SearchParameterDefinitionManager>()
                .Singleton()
                .AsSelf()
                .AsService<ISearchParameterDefinitionManager>()
                .AsService<IHostedService>();
            */

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

            /* Replace this
            services.Add<SearchParameterStatusManager>()
                .Singleton()
                .AsSelf()
                .ReplaceService<INotificationHandler<SearchParameterDefinitionManagerInitialized>>();

            // Replace this
            services.Add<SearchParameterSupportResolver>()
                .Singleton()
                .AsSelf()
                .AsImplementedInterfaces();
            */

            // TypedElement based converters
            // These always need to be added as they are also used by the SearchParameterSupportResolver
            // Exclude the extension converter, since it will be dynamically created by the converter manager
            services.TypesInSameAssemblyAs<ITypedElementToSearchValueConverter>()
                .AssignableTo<ITypedElementToSearchValueConverter>() // .Where(t => t.Type != typeof(FhirTypedElementToSearchValueConverterManager.ExtensionConverter))
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

            // services.AddSingleton<ISearchOptionsFactory, SearchOptionsFactory>();
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

            // Need a custom IModelServiceProvider
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public virtual void Configure(IApplicationBuilder app)
        {
            if (string.Equals(Configuration["ASPNETCORE_FORWARDEDHEADERS_ENABLED"], "true", StringComparison.OrdinalIgnoreCase))
            {
                app.UseForwardedHeaders();
            }

            /*
            app.UsePrometheusHttpMetrics();
            app.UseFhirServer();
            app.UseDevelopmentIdentityProviderIfConfigured();
            */
        }
    }
}
