// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Api.Features.Formatters;
using Microsoft.Health.Fhir.Api.Modules;
using Microsoft.Health.Fhir.Api.Modules.FeatureFlags.XmlFormatter;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Search.FhirPath;
using Microsoft.Health.Fhir.Core.Features.Validation;
using Microsoft.Health.Fhir.Ignixa;
using Microsoft.Health.Fhir.Ignixa.FhirPath;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Modules
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Web)]
    public class SdkModeModuleTests
    {
        [Fact]
        public void GivenFirelyMode_WhenFhirModuleLoads_ThenFirelyFormattersAndFhirPathRemainActive()
        {
            using var serviceProvider = BuildServiceProvider(FhirSdkMode.Firely);

            var mvcOptions = serviceProvider.GetRequiredService<IOptions<MvcOptions>>().Value;
            var fhirPathProvider = serviceProvider.GetRequiredService<IFhirPathProvider>();

            Assert.DoesNotContain(mvcOptions.InputFormatters, x => x.GetType().Name == "IgnixaFhirJsonInputFormatter");
            Assert.DoesNotContain(mvcOptions.OutputFormatters, x => x.GetType().Name == "IgnixaFhirJsonOutputFormatter");
            Assert.Contains(mvcOptions.InputFormatters, x => x is FhirJsonInputFormatter);
            Assert.Contains(mvcOptions.OutputFormatters, x => x is FhirJsonOutputFormatter);
            Assert.IsType<FirelyFhirPathProvider>(fhirPathProvider);
        }

        [Fact]
        public void GivenIgnixaMode_WhenFhirModuleLoads_ThenIgnixaFormattersAndFhirPathAreActive()
        {
            using var serviceProvider = BuildServiceProvider(FhirSdkMode.Ignixa);

            var mvcOptions = serviceProvider.GetRequiredService<IOptions<MvcOptions>>().Value;
            var fhirPathProvider = serviceProvider.GetRequiredService<IFhirPathProvider>();

            Assert.Equal("IgnixaFhirJsonInputFormatter", mvcOptions.InputFormatters[0].GetType().Name);
            Assert.Equal("IgnixaFhirJsonOutputFormatter", mvcOptions.OutputFormatters[0].GetType().Name);
            Assert.Contains(mvcOptions.InputFormatters, x => x is FhirXmlInputFormatter);
            Assert.Contains(mvcOptions.OutputFormatters, x => x is FhirXmlOutputFormatter);
            Assert.IsType<IgnixaFhirPathProvider>(fhirPathProvider);
        }

        [Fact]
        public void GivenFirelyMode_WhenValidationModuleLoads_ThenFirelyValidatorIsActive()
        {
            using var serviceProvider = BuildValidationServiceProvider(FhirSdkMode.Firely);

            var validator = serviceProvider.GetRequiredService<IModelAttributeValidator>();

            Assert.IsType<ModelAttributeValidator>(validator);
        }

        [Fact]
        public void GivenIgnixaMode_WhenValidationModuleLoads_ThenIgnixaValidatorIsActive()
        {
            using var serviceProvider = BuildValidationServiceProvider(FhirSdkMode.Ignixa);

            var validator = serviceProvider.GetRequiredService<IModelAttributeValidator>();

            Assert.IsType<IgnixaResourceValidator>(validator);
        }

        private static ServiceProvider BuildServiceProvider(FhirSdkMode mode)
        {
            var configuration = new FhirServerConfiguration();
            configuration.Sdk.Mode = mode;
            configuration.Features.SupportsXml = true;

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddOptions();
            services.AddMvcCore();
            new XmlFormatterFeatureModule(configuration).Load(services);
            new SearchModule(configuration).Load(services);
            new FhirModule(configuration).Load(services);

            return services.BuildServiceProvider(validateScopes: true);
        }

        private static ServiceProvider BuildValidationServiceProvider(FhirSdkMode mode)
        {
            var configuration = new FhirServerConfiguration();
            configuration.Sdk.Mode = mode;

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddOptions();
            services.AddMvcCore();
            new FhirModule(configuration).Load(services);
            new ValidationModule(configuration).Load(services);

            return services.BuildServiceProvider(validateScopes: true);
        }
    }
}
