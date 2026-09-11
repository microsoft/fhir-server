// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Hl7.Fhir.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Core.Features.Security;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Api.Modules;
using Microsoft.Health.Fhir.Core;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Compartment;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Operations.Import;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Modules
{
    [Trait(Traits.OwningTeam, OwningTeam.FhirImport)]
    [Trait(Traits.Category, Categories.Import)]
    public class OperationsModuleTests
    {
        [Fact]
        public void GivenDefaultConfiguration_WhenModuleLoads_ThenFirelyParserIsRegistered()
        {
            var configuration = new FhirServerConfiguration();
            var services = new ServiceCollection();

            new OperationsModule(configuration).Load(services);

            ServiceDescriptor descriptor = Assert.Single(
                services, x => x.ServiceType == typeof(IImportResourceParser));
            Assert.Equal("FirelyImportResourceParser", descriptor.ImplementationType.Name);
            Assert.Contains(
                services,
                x => x.ServiceType == typeof(IHostedService) &&
                    x.ImplementationType == typeof(FhirSdkProviderStartupLogger));
        }

        [Fact]
        public void GivenIgnixaConfiguration_WhenModuleLoads_ThenIgnixaParserIsRegistered()
        {
            var configuration = new FhirServerConfiguration();
            configuration.CoreFeatures.FhirSdkProvider.Import = FhirSdkProvider.Ignixa;
            var services = new ServiceCollection();

            new OperationsModule(configuration).Load(services);

            ServiceDescriptor descriptor = Assert.Single(
                services, x => x.ServiceType == typeof(IImportResourceParser));
            Assert.Equal("IgnixaImportResourceParser", descriptor.ImplementationType.Name);
        }

        [Fact]
        public void GivenUnknownProvider_WhenModuleLoads_ThenStartupFails()
        {
            var configuration = new FhirServerConfiguration();
            configuration.CoreFeatures.FhirSdkProvider.Import = (FhirSdkProvider)999;

            Assert.Throws<InvalidOperationException>(
                () => new OperationsModule(configuration).Load(new ServiceCollection()));
        }

        [Theory]
        [InlineData(FhirSdkProvider.Firely, true)]
        [InlineData(FhirSdkProvider.Ignixa, false)]
        public void GivenConfiguredProvider_WhenResolvedParserParsesContainedConditionalReference_ThenObservableResultProvesProviderPath(
            FhirSdkProvider provider,
            bool shouldReject)
        {
            const string json = """
                {
                  "resourceType":"Patient",
                  "id":"patient-1",
                  "contained":[{
                    "resourceType":"Observation",
                    "id":"obs-1",
                    "subject":{"reference":"Patient?identifier=system|value"},
                    "status":"final",
                    "code":{"text":"test"}
                  }]
                }
                """;
            using ServiceProvider serviceProvider = CreateParserServiceProvider(provider);

            IImportResourceParser parser = serviceProvider.GetRequiredService<IImportResourceParser>();
            Exception exception = Record.Exception(
                () => parser.Parse(0, 0, json.Length, json, ImportMode.InitialLoad));

            Assert.Equal(shouldReject, exception is not null);
            if (shouldReject)
            {
                Assert.IsType<NotSupportedException>(exception);
            }
        }

        private static ServiceProvider CreateParserServiceProvider(FhirSdkProvider provider)
        {
            var configuration = new FhirServerConfiguration();
            configuration.CoreFeatures.FhirSdkProvider.Import = provider;
            var services = new ServiceCollection();
            var requestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            requestContextAccessor.RequestContext.Method.Returns("PUT");
            requestContextAccessor.RequestContext.Uri.Returns(new Uri("https://unittest/Patient/123"));

            new OperationsModule(configuration).Load(services);
            services.AddSingleton(requestContextAccessor);
            services.AddSingleton<IClaimsExtractor>(Substitute.For<IClaimsExtractor>());
            services.AddSingleton<ICompartmentIndexer>(Substitute.For<ICompartmentIndexer>());
            services.AddSingleton<ISearchIndexer>(Substitute.For<ISearchIndexer>());
            services.AddSingleton<ISearchParameterDefinitionManager>(Substitute.For<ISearchParameterDefinitionManager>());
            services.AddSingleton<IModelInfoProvider, VersionSpecificModelInfoProvider>();
#pragma warning disable CS0618 // Type or member is obsolete
            services.AddSingleton(new FhirJsonParser(new ParserSettings() { PermissiveParsing = true, TruncateDateTimeToDate = true }));
#pragma warning restore CS0618 // Type or member is obsolete
            services.AddSingleton<IRawResourceFactory>(new RawResourceFactory(new FhirJsonSerializer()));
            services.AddSingleton<IResourceDeserializer>(Substitute.For<IResourceDeserializer>());
            services.AddSingleton<IResourceWrapperFactory, ResourceWrapperFactory>();

            return services.BuildServiceProvider();
        }
    }
}
