// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Api.Modules;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations.Import;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
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
            configuration.CoreFeatures.FhirSdkProvider.Default = FhirSdkProvider.Ignixa;
            var services = new ServiceCollection();

            new OperationsModule(configuration).Load(services);

            ServiceDescriptor descriptor = Assert.Single(
                services, x => x.ServiceType == typeof(IImportResourceParser));
            Assert.Equal("IgnixaImportResourceParser", descriptor.ImplementationType.Name);
        }

        [Fact]
        public void GivenAnImportOverride_WhenModuleLoads_ThenTheOverrideWinsOverTheDefault()
        {
            // Import can be moved on its own, and can equally be held back while the default moves.
            var configuration = new FhirServerConfiguration();
            configuration.CoreFeatures.FhirSdkProvider.Default = FhirSdkProvider.Ignixa;
            configuration.CoreFeatures.FhirSdkProvider.Import = FhirSdkProvider.Firely;
            var services = new ServiceCollection();

            new OperationsModule(configuration).Load(services);

            ServiceDescriptor descriptor = Assert.Single(
                services, x => x.ServiceType == typeof(IImportResourceParser));
            Assert.Equal("FirelyImportResourceParser", descriptor.ImplementationType.Name);
        }

        [Fact]
        public void GivenUnknownProvider_WhenModuleLoads_ThenStartupFails()
        {
            var configuration = new FhirServerConfiguration();
            configuration.CoreFeatures.FhirSdkProvider.Default = (FhirSdkProvider)999;

            Assert.Throws<InvalidOperationException>(
                () => new OperationsModule(configuration).Load(new ServiceCollection()));
        }
    }
}
