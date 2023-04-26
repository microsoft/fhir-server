// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DotLiquid;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations.ConvertData;
using Microsoft.Health.Fhir.Core.Features.Operations.ConvertData.Models;
using Microsoft.Health.Fhir.Core.Messages.ConvertData;
using Microsoft.Health.Fhir.Liquid.Converter.Exceptions;
using Microsoft.Health.Fhir.TemplateManagement.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using DataType = Microsoft.Health.Fhir.Liquid.Converter.Models.DataType;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Shared.Core.UnitTests.Features.Operations.ConvertData
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.DataConversion)]
    public class ConvertDataEngineTests
    {
        private ConvertDataConfiguration _config;

        public ConvertDataEngineTests()
        {
            _config = new ConvertDataConfiguration
            {
                Enabled = true,
                OperationTimeout = TimeSpan.FromSeconds(1),
            };
            _config.ContainerRegistryServers.Add("test.azurecr.io");
        }

        [Fact]
        public async Task GivenHl7V2ConvertDataRequest_WithDefaultTemplates_CorrectResultShouldReturn()
        {
            var convertDataEngine = GetDefaultEngine();
            var request = GetHl7V2RequestWithDefaultTemplates();
            var response = await convertDataEngine.Process(request, CancellationToken.None);

            var setting = new ParserSettings()
            {
                AcceptUnknownMembers = true,
                PermissiveParsing = true,
            };
            var parser = new FhirJsonParser(setting);
            var bundleResource = parser.Parse<Bundle>(response.Resource);

            var patient = bundleResource.Entry.ByResourceType<Patient>().First();
            Assert.NotEmpty(patient.Id);
            Assert.Equal("DUCK", patient.Name.First().Family);
            Assert.Equal("1924-10-10", patient.BirthDate);
        }

        [Fact]
        public async Task GivenCcdaConvertDataRequest_WithADefaultTemplates_CorrectResultShouldReturn()
        {
            var convertDataEngine = GetDefaultEngine();
            var request = GetCcdaRequestWithDefaultTemplates();
            var response = await convertDataEngine.Process(request, CancellationToken.None);

            var setting = new ParserSettings()
            {
                AcceptUnknownMembers = true,
                PermissiveParsing = true,
            };
            var parser = new FhirJsonParser(setting);
            var bundleResource = parser.Parse<Bundle>(response.Resource);

            var patient = bundleResource.Entry.ByResourceType<Patient>().First();
            Assert.NotEmpty(patient.Id);
            Assert.Equal("Nelson", patient.Name.First().Family);
            Assert.Equal("1962-08-28", patient.BirthDate);
        }

        [Fact]
        public async Task GivenJsonConvertDataRequest_WithADefaultTemplates_CorrectResultShouldReturn()
        {
            var convertDataEngine = GetDefaultEngine();
            var request = GetJsonRequestWithDefaultTemplates();
            var response = await convertDataEngine.Process(request, CancellationToken.None);

            var setting = new ParserSettings()
            {
                AcceptUnknownMembers = true,
                PermissiveParsing = true,
            };
            var parser = new FhirJsonParser(setting);
            var patient = parser.Parse<Patient>(response.Resource);

            Assert.NotEmpty(patient.Id);
            Assert.Equal("Smith", patient.Name.First().Family);
            Assert.Equal("2001-01-10", patient.BirthDate);
        }

        [Fact]
        public async Task GivenFhirConvertDataRequest_WithADefaultTemplates_CorrectResultShouldReturn()
        {
            var convertDataEngine = GetDefaultEngine();
            var request = GetFhirRequestWithDefaultTemplates();
            var response = await convertDataEngine.Process(request, CancellationToken.None);

            var setting = new ParserSettings()
            {
                AcceptUnknownMembers = true,
                PermissiveParsing = true,
            };
            var parser = new FhirJsonParser(setting);
            var patient = parser.Parse<Patient>(response.Resource);

            Assert.NotEmpty(patient.Id);
            Assert.Single(patient.Extension);
        }

        [Theory]
        [InlineData("fakeacr.azurecr.io/template:default")]
        [InlineData("test.azurecr-test.io/template:default")]
        [InlineData("test.azurecr.com/template@sha256:592535ef52d742f81e35f4d87b43d9b535ed56cf58c90a14fc5fd7ea0fbb8696")]
        [InlineData("*****####.com/template:default")]
        [InlineData("¶Š™œãý£¾.com/template:default")]
        public async Task GivenConvertDataRequest_WithUnconfiguredRegistry_ContainerRegistryNotConfiguredExceptionShouldBeThrown(string templateReference)
        {
            var convertDataEngine = GetCustomEngine();
            var hl7v2Request = GetHl7V2RequestWithTemplateReference(templateReference);
            await Assert.ThrowsAsync<ContainerRegistryNotConfiguredException>(() => convertDataEngine.Process(hl7v2Request, CancellationToken.None));

            var ccdaRequest = GetCcdaRequestWithTemplateReference(templateReference);
            await Assert.ThrowsAsync<ContainerRegistryNotConfiguredException>(() => convertDataEngine.Process(ccdaRequest, CancellationToken.None));

            var jsonRequest = GetJsonRequestWithTemplateReference(templateReference);
            await Assert.ThrowsAsync<ContainerRegistryNotConfiguredException>(() => convertDataEngine.Process(jsonRequest, CancellationToken.None));

            var fhirRequest = GetFhirRequestWithTemplateReference(templateReference);
            await Assert.ThrowsAsync<ContainerRegistryNotConfiguredException>(() => convertDataEngine.Process(fhirRequest, CancellationToken.None));
        }

        [Theory]
        [InlineData("       ")]
        [InlineData("Abc")]
        [InlineData("¶Š™œãý£¾")]
        [InlineData("MSH|")]
        public async Task GivenHl7V2ConvertDataRequest_WithWrongInputData_ConvertDataFailedExceptionShouldBeThrown(string inputData)
        {
            var convertDataEngine = GetDefaultEngine();
            var request = GetHl7V2RequestWithInputData(inputData);
            var exception = await Assert.ThrowsAsync<ConvertDataFailedException>(() => convertDataEngine.Process(request, CancellationToken.None));
            Assert.True(exception.InnerException is DataParseException);
        }

        [Theory]
        [InlineData("       ")]
        [InlineData("Abc")]
        [InlineData("¶Š™œãý£¾")]
        [InlineData("<?xml version=\"1.0\"?><?xml-stylesheet type='text/xsl' href=''?>")]
        public async Task GivenCcdaConvertDataRequest_WithWrongInputData_ConvertDataFailedExceptionShouldBeThrown(string inputData)
        {
            var convertDataEngine = GetDefaultEngine();
            var request = GetCcdaRequestWithInputData(inputData);
            var exception = await Assert.ThrowsAsync<ConvertDataFailedException>(() => convertDataEngine.Process(request, CancellationToken.None));
            Assert.True(exception.InnerException is DataParseException);
        }

        [Theory]
        [InlineData("       ")]
        [InlineData("Abc")]
        [InlineData("¶Š™œãý£¾")]
        [InlineData("{\"a\"}")]
        public async Task GivenJsonConvertDataRequest_WithWrongInputData_ConvertDataFailedExceptionShouldBeThrown(string inputData)
        {
            var convertDataEngine = GetDefaultEngine();
            var request = GetJsonRequestWithInputData(inputData);
            var exception = await Assert.ThrowsAsync<ConvertDataFailedException>(() => convertDataEngine.Process(request, CancellationToken.None));
            Assert.True(exception.InnerException is DataParseException);
        }

        [Theory]
        [InlineData("       ")]
        [InlineData("Abc")]
        [InlineData("¶Š™œãý£¾")]
        [InlineData("{\"a\"}")]
        public async Task GivenFhirConvertDataRequest_WithWrongInputData_ConvertDataFailedExceptionShouldBeThrown(string inputData)
        {
            var convertDataEngine = GetDefaultEngine();
            var request = GetFhirRequestWithInputData(inputData);
            var exception = await Assert.ThrowsAsync<ConvertDataFailedException>(() => convertDataEngine.Process(request, CancellationToken.None));
            Assert.True(exception.InnerException is DataParseException);
        }

        [Theory]
        [InlineData("       ")]
        [InlineData("ADT")]
        [InlineData("¶Š™œãý£¾")]
        public async Task GivenHl7V2ConvertDataRequest_WithWrongRootTemplate_ConvertDataFailedExceptionShouldBeThrown(string rootTemplateName)
        {
            var convertDataEngine = GetDefaultEngine();
            var request = GetHl7V2RequestWithRootTemplate(rootTemplateName);
            var exception = await Assert.ThrowsAsync<ConvertDataFailedException>(() => convertDataEngine.Process(request, CancellationToken.None));
            Assert.Equal($"Template '{rootTemplateName}' not found", exception.InnerException.Message);
        }

        [Theory]
        [InlineData("       ")]
        [InlineData("CCD1")]
        [InlineData("¶Š™œãý£¾")]
        public async Task GivenCcdaConvertDataRequest_WithWrongRootTemplate_ConvertDataFailedExceptionShouldBeThrown(string rootTemplateName)
        {
            var convertDataEngine = GetDefaultEngine();
            var request = GetCcdaRequestWithRootTemplate(rootTemplateName);
            var exception = await Assert.ThrowsAsync<ConvertDataFailedException>(() => convertDataEngine.Process(request, CancellationToken.None));
            Assert.Equal($"Template '{rootTemplateName}' not found", exception.InnerException.Message);
        }

        [Theory]
        [InlineData("       ")]
        [InlineData("Example")]
        [InlineData("¶Š™œãý£¾")]
        public async Task GivenJsonConvertDataRequest_WithWrongRootTemplate_ConvertDataFailedExceptionShouldBeThrown(string rootTemplateName)
        {
            var convertDataEngine = GetDefaultEngine();
            var request = GetJsonRequestWithRootTemplate(rootTemplateName);
            var exception = await Assert.ThrowsAsync<ConvertDataFailedException>(() => convertDataEngine.Process(request, CancellationToken.None));
            Assert.Equal($"Template '{rootTemplateName}' not found", exception.InnerException.Message);
        }

        [Theory]
        [InlineData("       ")]
        [InlineData("Example")]
        [InlineData("¶Š™œãý£¾")]
        public async Task GivenFhirConvertDataRequest_WithWrongRootTemplate_ConvertDataFailedExceptionShouldBeThrown(string rootTemplateName)
        {
            var convertDataEngine = GetDefaultEngine();
            var request = GetFhirRequestWithRootTemplate(rootTemplateName);
            var exception = await Assert.ThrowsAsync<ConvertDataFailedException>(() => convertDataEngine.Process(request, CancellationToken.None));
            Assert.Equal($"Template '{rootTemplateName}' not found", exception.InnerException.Message);
        }

        [Fact]
        public async Task GivenHl7V2TemplateNotInJsonFormat_WhenConvert_ExceptionShouldBeThrown()
        {
            var wrongTemplateCollection = new List<Dictionary<string, Template>>
            {
                new Dictionary<string, Template>
                {
                    { "ADT_A01", Template.Parse(@"""a"":""b""") },
                },
            };
            var convertDataEngine = GetDefaultEngineWithTemplates(wrongTemplateCollection);
            var request = GetHl7V2RequestWithDefaultTemplates();
            var exception = await Assert.ThrowsAsync<ConvertDataFailedException>(() => convertDataEngine.Process(request, CancellationToken.None));
            Assert.True(exception.InnerException is PostprocessException);
        }

        [Fact]
        public async Task GivenCcdaTemplateNotInJsonFormat_WhenConvert_ExceptionShouldBeThrown()
        {
            var wrongTemplateCollection = new List<Dictionary<string, Template>>
            {
                new Dictionary<string, Template>
                {
                    { "CCD", Template.Parse(@"""a"":""b""") },
                },
            };
            var convertDataEngine = GetDefaultEngineWithTemplates(wrongTemplateCollection);
            var request = GetCcdaRequestWithDefaultTemplates();
            var exception = await Assert.ThrowsAsync<ConvertDataFailedException>(() => convertDataEngine.Process(request, CancellationToken.None));
            Assert.True(exception.InnerException is PostprocessException);
        }

        [Fact]
        public async Task GivenJsonTemplateNotInJsonFormat_WhenConvert_ExceptionShouldBeThrown()
        {
            var wrongTemplateCollection = new List<Dictionary<string, Template>>
            {
                new Dictionary<string, Template>
                {
                    { "ExamplePatient", Template.Parse(@"""a"":""b""") },
                },
            };
            var convertDataEngine = GetDefaultEngineWithTemplates(wrongTemplateCollection);
            var request = GetJsonRequestWithDefaultTemplates();
            var exception = await Assert.ThrowsAsync<ConvertDataFailedException>(() => convertDataEngine.Process(request, CancellationToken.None));
            Assert.True(exception.InnerException is PostprocessException);
        }

        [Fact]
        public async Task GivenFhirTemplateNotInJsonFormat_WhenConvert_ExceptionShouldBeThrown()
        {
            var wrongTemplateCollection = new List<Dictionary<string, Template>>
            {
                new Dictionary<string, Template>
                {
                    { "Patient", Template.Parse(@"""a"":""b""") },
                },
            };
            var convertDataEngine = GetDefaultEngineWithTemplates(wrongTemplateCollection);
            var request = GetFhirRequestWithDefaultTemplates();
            var exception = await Assert.ThrowsAsync<ConvertDataFailedException>(() => convertDataEngine.Process(request, CancellationToken.None));
            Assert.True(exception.InnerException is PostprocessException);
        }

        private static ConvertDataRequest GetHl7V2RequestWithDefaultTemplates()
        {
            return new ConvertDataRequest(Samples.SampleHl7v2Message, DataType.Hl7v2, "microsofthealth", true, GetDefaultTemplateImageReferenceByDataType(DataType.Hl7v2), "ADT_A01");
        }

        private static ConvertDataRequest GetCcdaRequestWithDefaultTemplates()
        {
            return new ConvertDataRequest(Samples.SampleCcdaMessage, DataType.Ccda, "microsofthealth", true, GetDefaultTemplateImageReferenceByDataType(DataType.Ccda), "CCD");
        }

        private static ConvertDataRequest GetJsonRequestWithDefaultTemplates()
        {
            return new ConvertDataRequest(Samples.SampleJsonMessage, DataType.Json, "microsofthealth", true, GetDefaultTemplateImageReferenceByDataType(DataType.Json), "ExamplePatient");
        }

        private static ConvertDataRequest GetFhirRequestWithDefaultTemplates()
        {
            return new ConvertDataRequest(Samples.SampleFhirStu3Message, DataType.Fhir, "microsofthealth", true, GetDefaultTemplateImageReferenceByDataType(DataType.Fhir), "Patient");
        }

        private static ConvertDataRequest GetHl7V2RequestWithTemplateReference(string imageReference)
        {
            return new ConvertDataRequest(Samples.SampleHl7v2Message, DataType.Hl7v2, imageReference.Split('/')[0], false, imageReference, "ADT_A01");
        }

        private static ConvertDataRequest GetCcdaRequestWithTemplateReference(string imageReference)
        {
            return new ConvertDataRequest(Samples.SampleCcdaMessage, DataType.Ccda, imageReference.Split('/')[0], false, imageReference, "CCD");
        }

        private static ConvertDataRequest GetJsonRequestWithTemplateReference(string imageReference)
        {
            return new ConvertDataRequest(Samples.SampleJsonMessage, DataType.Json, imageReference.Split('/')[0], false, imageReference, "ExamplePatient");
        }

        private static ConvertDataRequest GetFhirRequestWithTemplateReference(string imageReference)
        {
            return new ConvertDataRequest(Samples.SampleFhirStu3Message, DataType.Fhir, imageReference.Split('/')[0], false, imageReference, "Patient");
        }

        private static ConvertDataRequest GetHl7V2RequestWithInputData(string inputData)
        {
            return new ConvertDataRequest(inputData, DataType.Hl7v2, "microsofthealth", true, GetDefaultTemplateImageReferenceByDataType(DataType.Hl7v2), "ADT_A01");
        }

        private static ConvertDataRequest GetCcdaRequestWithInputData(string inputData)
        {
            return new ConvertDataRequest(inputData, DataType.Ccda, "microsofthealth", true, GetDefaultTemplateImageReferenceByDataType(DataType.Ccda), "CCD");
        }

        private static ConvertDataRequest GetJsonRequestWithInputData(string inputData)
        {
            return new ConvertDataRequest(inputData, DataType.Json, "microsofthealth", true, GetDefaultTemplateImageReferenceByDataType(DataType.Json), "ExamplePatient");
        }

        private static ConvertDataRequest GetFhirRequestWithInputData(string inputData)
        {
            return new ConvertDataRequest(inputData, DataType.Fhir, "microsofthealth", true, GetDefaultTemplateImageReferenceByDataType(DataType.Fhir), "Patient");
        }

        private static ConvertDataRequest GetHl7V2RequestWithRootTemplate(string rootTemplate)
        {
            return new ConvertDataRequest(Samples.SampleHl7v2Message, DataType.Hl7v2, "microsofthealth", true, GetDefaultTemplateImageReferenceByDataType(DataType.Hl7v2), rootTemplate);
        }

        private static ConvertDataRequest GetCcdaRequestWithRootTemplate(string rootTemplate)
        {
            return new ConvertDataRequest(Samples.SampleCcdaMessage, DataType.Ccda, "microsofthealth", true, GetDefaultTemplateImageReferenceByDataType(DataType.Ccda), rootTemplate);
        }

        private static ConvertDataRequest GetJsonRequestWithRootTemplate(string rootTemplate)
        {
            return new ConvertDataRequest(Samples.SampleJsonMessage, DataType.Json, "microsofthealth", true, GetDefaultTemplateImageReferenceByDataType(DataType.Json), rootTemplate);
        }

        private static ConvertDataRequest GetFhirRequestWithRootTemplate(string rootTemplate)
        {
            return new ConvertDataRequest(Samples.SampleFhirStu3Message, DataType.Fhir, "microsofthealth", true, GetDefaultTemplateImageReferenceByDataType(DataType.Fhir), rootTemplate);
        }

        private IConvertDataEngine GetDefaultEngine()
        {
            IOptions<ConvertDataConfiguration> convertDataConfiguration = Options.Create(_config);

            DefaultTemplateProvider templateProvider = new DefaultTemplateProvider(convertDataConfiguration, new NullLogger<DefaultTemplateProvider>());
            ITemplateProviderFactory templateProviderFactory = Substitute.For<ITemplateProviderFactory>();
            templateProviderFactory.GetDefaultTemplateProvider().ReturnsForAnyArgs(templateProvider);

            return new ConvertDataEngine(
                templateProviderFactory,
                convertDataConfiguration,
                new NullLogger<ConvertDataEngine>());
        }

        private IConvertDataEngine GetDefaultEngineWithTemplates(List<Dictionary<string, Template>> templateCollection)
        {
            var templateProviderFactory = Substitute.For<ITemplateProviderFactory>();

            IConvertDataTemplateProvider templateProvider = Substitute.For<IConvertDataTemplateProvider>();

            templateProvider.GetTemplateCollectionAsync(default, default).ReturnsForAnyArgs(templateCollection);
            templateProviderFactory.GetDefaultTemplateProvider().ReturnsForAnyArgs(templateProvider);
            return new ConvertDataEngine(templateProviderFactory, Options.Create(_config), new NullLogger<ConvertDataEngine>());
        }

        private IConvertDataEngine GetCustomEngine()
        {
            IOptions<ConvertDataConfiguration> convertDataConfiguration = Options.Create(_config);
            IContainerRegistryTokenProvider containerRegistryTokenProvider = Substitute.For<IContainerRegistryTokenProvider>();
            containerRegistryTokenProvider.GetTokenAsync(Arg.Any<string>(), default).ReturnsForAnyArgs(x => GetToken(x[0].ToString(), _config));

            ContainerRegistryTemplateProvider templateProvider = new ContainerRegistryTemplateProvider(containerRegistryTokenProvider, convertDataConfiguration, new NullLogger<ContainerRegistryTemplateProvider>());
            ITemplateProviderFactory templateProviderFactory = Substitute.For<ITemplateProviderFactory>();
            templateProviderFactory.GetContainerRegistryTemplateProvider().ReturnsForAnyArgs(templateProvider);

            return new ConvertDataEngine(
                templateProviderFactory,
                convertDataConfiguration,
                new NullLogger<ConvertDataEngine>());
        }

        private IConvertDataEngine GetCustomEngineWithTemplates(List<Dictionary<string, Template>> templateCollection)
        {
            var templateProviderFactory = Substitute.For<ITemplateProviderFactory>();
            var templateProvider = Substitute.For<ContainerRegistryTemplateProvider>();
            templateProvider.GetTemplateCollectionAsync(default, default).ReturnsForAnyArgs(templateCollection);
            templateProviderFactory.GetContainerRegistryTemplateProvider().ReturnsForAnyArgs(templateProvider);
            return new ConvertDataEngine(templateProviderFactory, Options.Create(_config), new NullLogger<ConvertDataEngine>());
        }

        // For unit tests, we only use the built-in templates and here returns an empty token.
        private string GetToken(string registry, ConvertDataConfiguration config)
        {
            if (!config.ContainerRegistryServers.Any(server => string.Equals(server, registry, StringComparison.OrdinalIgnoreCase)))
            {
                throw new ContainerRegistryNotConfiguredException("Container registry not configured.");
            }

            return string.Empty;
        }

        private static string GetDefaultTemplateImageReferenceByDataType(DataType dataType)
        {
            return DefaultTemplateInfo.DefaultTemplateMap.Values.FirstOrDefault(value => value.DataType == dataType).ImageReference;
        }
    }
}
