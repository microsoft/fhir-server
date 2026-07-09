// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Api.Features.ContentTypes;
using Microsoft.Health.Fhir.Api.Features.Formatters;
using Microsoft.Health.Fhir.Api.Modules;
using Microsoft.Health.Fhir.Api.Modules.FeatureFlags.XmlFormatter;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Conformance.Models;
using Microsoft.Health.Fhir.Core.Features.Search.FhirPath;
using Microsoft.Health.Fhir.Core.Features.Validation;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Ignixa;
using Microsoft.Health.Fhir.Ignixa.FhirPath;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
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
            Assert.Contains(mvcOptions.InputFormatters, x => x is FhirXmlInputFormatter);
            Assert.Contains(mvcOptions.OutputFormatters, x => x is FhirXmlOutputFormatter);
            Assert.IsType<FirelyFhirPathProvider>(fhirPathProvider);
        }

        [Fact]
        public void GivenIgnixaMode_WhenFhirModuleLoads_ThenIgnixaJsonFormattersAndFhirPathAreActiveButXmlIsDisabled()
        {
            using var serviceProvider = BuildServiceProvider(FhirSdkMode.Ignixa);

            var mvcOptions = serviceProvider.GetRequiredService<IOptions<MvcOptions>>().Value;
            var fhirPathProvider = serviceProvider.GetRequiredService<IFhirPathProvider>();

            Assert.Equal("IgnixaFhirJsonInputFormatter", mvcOptions.InputFormatters[0].GetType().Name);
            Assert.Equal("IgnixaFhirJsonOutputFormatter", mvcOptions.OutputFormatters[0].GetType().Name);
            Assert.DoesNotContain(mvcOptions.InputFormatters, x => x is FhirXmlInputFormatter);
            Assert.DoesNotContain(mvcOptions.OutputFormatters, x => x is FhirXmlOutputFormatter);
            Assert.IsType<IgnixaFhirPathProvider>(fhirPathProvider);
        }

        [Fact]
        public void GivenHybridMode_WhenFhirModuleLoads_ThenIgnixaJsonAndXmlFormattersRemainActive()
        {
            using var serviceProvider = BuildServiceProvider(FhirSdkMode.Hybrid);

            var mvcOptions = serviceProvider.GetRequiredService<IOptions<MvcOptions>>().Value;

            Assert.Equal("IgnixaFhirJsonInputFormatter", mvcOptions.InputFormatters[0].GetType().Name);
            Assert.Equal("IgnixaFhirJsonOutputFormatter", mvcOptions.OutputFormatters[0].GetType().Name);
            Assert.Contains(mvcOptions.InputFormatters, x => x is FhirXmlInputFormatter);
            Assert.Contains(mvcOptions.OutputFormatters, x => x is FhirXmlOutputFormatter);
        }

        [Theory]
        [InlineData(FhirSdkMode.Ignixa)]
        [InlineData(FhirSdkMode.Hybrid)]
        public void GivenIgnixaActiveMode_WhenFhirModuleDeserializerIsUsed_ThenResourceJsonNodeIsPreserved(FhirSdkMode mode)
        {
            using var serviceProvider = BuildServiceProvider(mode);
            var deserializers = serviceProvider.GetRequiredService<IReadOnlyDictionary<FhirResourceFormat, Func<string, string, DateTimeOffset, ResourceElement>>>();
            const string patientJson = "{\"resourceType\":\"Patient\",\"id\":\"module-deserializer\",\"active\":true}";

            var resourceElement = deserializers[FhirResourceFormat.Json](patientJson, "1", DateTimeOffset.UtcNow);

            Assert.NotNull(resourceElement.GetIgnixaNode());
        }

        [Fact]
        public async Task GivenFirelyModeWithXmlSupport_WhenXmlCapabilityBuilds_ThenXmlFormatsAreAdvertised()
        {
            var capabilityStatement = await BuildCapabilityStatementAsync(FhirSdkMode.Firely);

            Assert.Contains(KnownContentTypes.XmlContentType, capabilityStatement.Format);
            Assert.Contains("xml", capabilityStatement.Format);
        }

        [Fact]
        public async Task GivenIgnixaModeWithXmlSupport_WhenXmlCapabilityBuilds_ThenXmlFormatsAreNotAdvertised()
        {
            var capabilityStatement = await BuildCapabilityStatementAsync(FhirSdkMode.Ignixa);

            Assert.DoesNotContain(KnownContentTypes.XmlContentType, capabilityStatement.Format);
            Assert.DoesNotContain("xml", capabilityStatement.Format);
        }

        [Fact]
        public async Task GivenHybridModeWithXmlSupport_WhenXmlCapabilityBuilds_ThenXmlFormatsRemainAdvertised()
        {
            var capabilityStatement = await BuildCapabilityStatementAsync(FhirSdkMode.Hybrid);

            Assert.Contains(KnownContentTypes.XmlContentType, capabilityStatement.Format);
            Assert.Contains("xml", capabilityStatement.Format);
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

        private static async Task<ListedCapabilityStatement> BuildCapabilityStatementAsync(FhirSdkMode mode)
        {
            var configuration = new FhirServerConfiguration();
            configuration.Sdk.Mode = mode;
            configuration.Features.SupportsXml = true;

            var capabilityStatement = new ListedCapabilityStatement();
            var builder = Substitute.For<ICapabilityStatementBuilder>();
            builder
                .When(x => x.Apply(Arg.Any<Action<ListedCapabilityStatement>>()))
                .Do(x => x.Arg<Action<ListedCapabilityStatement>>().Invoke(capabilityStatement));

            var provider = new XmlFormatterConfiguration(Options.Create(configuration), Options.Create(configuration.Features));
            await provider.BuildAsync(builder, default);

            return capabilityStatement;
        }
    }
}
