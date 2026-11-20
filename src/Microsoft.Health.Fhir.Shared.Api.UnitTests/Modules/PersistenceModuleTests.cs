// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Hl7.Fhir.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Api.Modules;
using Microsoft.Health.Fhir.Core;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Ignixa;
using Microsoft.Health.Fhir.Ignixa.Features.Persistence;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Modules
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Serialization)]
    public class PersistenceModuleTests
    {
        [Theory]
        [InlineData(FhirSdkProvider.Firely, FhirSdkProvider.Firely, typeof(RawResourceFactory))]
        [InlineData(FhirSdkProvider.Firely, FhirSdkProvider.Ignixa, typeof(IgnixaRawResourceFactory))]
        [InlineData(FhirSdkProvider.Ignixa, null, typeof(IgnixaRawResourceFactory))]
        [InlineData(FhirSdkProvider.Ignixa, FhirSdkProvider.Firely, typeof(RawResourceFactory))]
        public void GivenProviderSettings_WhenModuleLoads_ThenTheSelectedWriterIsTheOnlySingleton(
            FhirSdkProvider systemDefault,
            FhirSdkProvider? persistenceWrite,
            Type expectedWriterType)
        {
            var configuration = new FhirServerConfiguration();
            configuration.CoreFeatures.FhirSdkProvider.SystemDefault = systemDefault;
            configuration.CoreFeatures.FhirSdkProvider.PersistenceWrite = persistenceWrite;
            ServiceCollection services = CreateServices();

            CreatePersistenceModule(configuration).Load(services);
            using ServiceProvider provider = services.BuildServiceProvider();
            IRawResourceFactory writer = provider.GetRequiredService<IRawResourceFactory>();

            Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IRawResourceFactory));
            Assert.IsType(expectedWriterType, writer);
            Assert.Same(writer, provider.GetRequiredService<IRawResourceFactory>());
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void GivenIgnixaImportAndWrites_WhenModulesLoadInEitherOrder_ThenTheyRegisterOneSchemaContext(bool importFirst)
        {
            var configuration = new FhirServerConfiguration();
            configuration.CoreFeatures.FhirSdkProvider.Import = FhirSdkProvider.Ignixa;
            configuration.CoreFeatures.FhirSdkProvider.PersistenceWrite = FhirSdkProvider.Ignixa;
            ServiceCollection services = CreateServices();
            var operations = new OperationsModule(configuration);
            PersistenceModule persistence = CreatePersistenceModule(configuration);

            if (importFirst)
            {
                operations.Load(services);
                persistence.Load(services);
            }
            else
            {
                persistence.Load(services);
                operations.Load(services);
            }

            using ServiceProvider provider = services.BuildServiceProvider();

            Assert.Single(provider.GetServices<IgnixaSchemaContext>());
            Assert.IsType<IgnixaRawResourceFactory>(provider.GetRequiredService<IRawResourceFactory>());
        }

        [Fact]
        public void GivenUnknownPersistenceWriteProvider_WhenModuleLoads_ThenStartupFails()
        {
            var configuration = new FhirServerConfiguration();
            configuration.CoreFeatures.FhirSdkProvider.PersistenceWrite = (FhirSdkProvider)999;
            PersistenceModule module = CreatePersistenceModule(configuration);

            Assert.Throws<InvalidOperationException>(() => module.Load(new ServiceCollection()));
        }

        private static ServiceCollection CreateServices()
        {
            var services = new ServiceCollection();
            services.AddSingleton(new FhirJsonSerializer());
            services.AddSingleton<IModelInfoProvider, VersionSpecificModelInfoProvider>();
            return services;
        }

        private static PersistenceModule CreatePersistenceModule(FhirServerConfiguration configuration)
        {
            var services = new ServiceCollection();
            services.AddSingleton(configuration);
            using ServiceProvider provider = services.BuildServiceProvider();
            return ActivatorUtilities.CreateInstance<PersistenceModule>(provider);
        }
    }
}
