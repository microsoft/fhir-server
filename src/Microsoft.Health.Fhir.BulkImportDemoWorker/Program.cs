// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Health.Core.Features.Security;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Compartment;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Converters;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.BulkImportDemoWorker
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
              Host.CreateDefaultBuilder(args)
              .ConfigureServices(services =>
              {
                  services.AddScoped<ResourceIdProvider>();

                  ModelExtensions.SetModelInfoProvider();

                  var jsonParser = new FhirJsonParser(DefaultParserSettings.Settings);
                  var jsonSerializer = new FhirJsonSerializer();

                  services.AddSingleton(jsonParser);
                  services.AddSingleton(jsonSerializer);

                  services.AddSingleton<IRawResourceFactory, RawResourceFactory>();
                  services.AddSingleton<IResourceWrapperFactory, ResourceWrapperFactory>();

                  services.AddSingleton<IClaimsExtractor, PrincipalClaimsExtractor>();

                  services.Add<CompartmentDefinitionManager>()
                    .Singleton()
                    .AsSelf()
                    .AsService<IHostedService>()
                    .AsService<ICompartmentDefinitionManager>();

                  services.Add<CompartmentIndexer>()
                      .Singleton()
                      .AsSelf()
                      .AsService<ICompartmentIndexer>();

                  services.Add<FhirRequestContextAccessor>()
                    .Singleton()
                    .AsSelf()
                    .AsService<IFhirRequestContextAccessor>();

                  services.Add<VersionSpecificModelInfoProvider>()
                    .Singleton()
                    .AsSelf()
                    .AsService<IModelInfoProvider>();

                  services.AddSingleton<IReferenceToElementResolver, LightweightReferenceToElementResolver>();
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

                  services.AddSingleton<ISearchIndexer, TypedElementSearchIndexer>();

                  services.AddHostedService<BulkImportBackEndService>();
              });
    }
}
