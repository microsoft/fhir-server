// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Hl7.FhirPath;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Api.Modules;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.FhirPath;
using Microsoft.Health.Fhir.Ignixa;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Modules
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    [Collection(FhirPathProviderTestCollection.Name)]
    public class SearchModuleTests : IDisposable
    {
        private readonly IFhirPathProvider _originalAmbientProvider = FhirPathProvider.Instance;

        [Fact]
        public void GivenDefaultConfiguration_WhenModuleLoads_ThenAmbientAndDependencyInjectionUseSameFirelySingleton()
        {
            var services = new ServiceCollection();

            new SearchModule(new FhirServerConfiguration()).Load(services);
            using ServiceProvider serviceProvider = services.BuildServiceProvider();

            IFhirPathProvider dependencyInjectionProvider = serviceProvider.GetRequiredService<IFhirPathProvider>();
            Assert.IsType<FirelyFhirPathProvider>(dependencyInjectionProvider);
            Assert.Same(FhirPathProvider.Instance, dependencyInjectionProvider);
        }

        [Fact]
        public void GivenUnknownFhirPathProvider_WhenModuleLoads_ThenStartupFails()
        {
            var configuration = new FhirServerConfiguration();
            configuration.CoreFeatures.FhirSdkProvider.FhirPath = (FhirSdkProvider)999;

            Assert.Throws<InvalidOperationException>(
                () => new SearchModule(configuration).Load(new ServiceCollection()));
        }

        [Fact]
        public void GivenIgnixaConfiguration_WhenModuleLoads_ThenFirelyPatchFunctionsRemainRegistered()
        {
            var configuration = new FhirServerConfiguration();
            configuration.CoreFeatures.FhirSdkProvider.FhirPath = FhirSdkProvider.Ignixa;
            var services = new ServiceCollection();

            new SearchModule(configuration).Load(services);
            using ServiceProvider serviceProvider = services.BuildServiceProvider();
            var compiler = new FhirPathCompiler();

            Assert.IsType<IgnixaFhirPathProvider>(serviceProvider.GetRequiredService<IFhirPathProvider>());
            Assert.NotNull(compiler.Compile("id.hasValue()"));
            Assert.NotNull(compiler.Compile("managingOrganization.resolve().hasValue()"));
        }

        public void Dispose()
            => FhirPathProvider.SetProviderFactory(() => _originalAmbientProvider);
    }
}
