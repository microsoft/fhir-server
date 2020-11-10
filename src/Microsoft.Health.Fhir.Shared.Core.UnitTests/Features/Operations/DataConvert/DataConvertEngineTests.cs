// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Azure.ContainerRegistry;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations.DataConvert;
using Microsoft.Health.Fhir.Core.Features.Operations.DataConvert.Models;
using Microsoft.Health.Fhir.Core.Messages.DataConvert;
using Microsoft.Health.Fhir.TemplateManagement;
using Microsoft.Health.Fhir.TemplateManagement.Models;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Shared.Core.UnitTests.Features.Operations.DataConvert
{
    public class DataConvertEngineTests
    {
        private const string DefaultTemplateReference = "microsofthealth/fhirconverter:default";

        [Fact]
        public async Task GivenDataConvertRequest_WithDefaultTemplates_CorrectResultShouldReturn()
        {
            var request = GetHl7V2RequestWithDefaultTemplates();
            var dataConvertEngine = GetDataConvertEngine();
            var response = await dataConvertEngine.Process(request, CancellationToken.None);

            var setting = new ParserSettings()
            {
                AcceptUnknownMembers = true,
                PermissiveParsing = true,
            };
            var parser = new FhirJsonParser(setting);
            var bundleResource = parser.Parse<Bundle>(response.Resource);
            Assert.Equal("urn:uuid:b06a26a8-9cb6-ef2c-b4a7-3781a6f7f71a", bundleResource.Entry.First().FullUrl);
            Assert.Equal(2, bundleResource.Entry.Count);

            var patient = bundleResource.Entry.First().Resource as Patient;
            Assert.Equal("Kinmonth", patient.Name.First().Family);
            Assert.Equal("1987-06-24", patient.BirthDate);
        }

        /// <summary>
        /// For unit tests, we only check the reference format.
        /// We will do real data check (registry/image/tag/digest not found) in E2E tests.
        /// </summary>
        [Theory]
        [InlineData("           ")]
        [InlineData("test.azurecr.io")]
        [InlineData("template:default")]
        [InlineData("/template:default")]
        public async Task GivenDataConvertRequest_WithWrongTemplateReference_TemplateReferenceInvalidExceptionShouldBeThrown(string templateReference)
        {
            var request = GetHl7V2RequestWithTemplateReference(templateReference);
            var dataConvertEngine = GetDataConvertEngine();
            await Assert.ThrowsAsync<TemplateReferenceInvalidException>(() => dataConvertEngine.Process(request, CancellationToken.None));
        }

        [Theory]
        [InlineData("fakeacr.azurecr.io/template:default")]
        [InlineData("test.azurecr-test.io/template:default")]
        [InlineData("test.azurecr.com/template@sha256:592535ef52d742f81e35f4d87b43d9b535ed56cf58c90a14fc5fd7ea0fbb8696")]
        [InlineData("*****####.com/template:default")]
        [InlineData("¶Š™œãý£¾.com/template:default")]
        public async Task GivenDataConvertRequest_WithUnregisteredRegistry_ContainerRegistryNotRegisteredExceptionShouldBeThrown(string templateReference)
        {
            var request = GetHl7V2RequestWithTemplateReference(templateReference);
            var dataConvertEngine = GetDataConvertEngine();
            await Assert.ThrowsAsync<ContainerRegistryNotRegisteredException>(() => dataConvertEngine.Process(request, CancellationToken.None));
        }

        [Theory]
        [InlineData("       ")]
        [InlineData("Abc")]
        [InlineData("¶Š™œãý£¾")]
        [InlineData("MSH|")]
        public async Task GivenDataConvertRequest_WithWrongInputData_DataConvertFailedExceptionShouldBeThrown(string inputData)
        {
            var request = GetHl7V2RequestWithInputData(inputData);
            var dataConvertEngine = GetDataConvertEngine();
            await Assert.ThrowsAsync<InputDataParseErrorException>(() => dataConvertEngine.Process(request, CancellationToken.None));
        }

        [Theory]
        [InlineData("       ")]
        [InlineData("ADT_A02")]
        [InlineData("¶Š™œãý£¾")]
        public async Task GivenDataConvertRequest_WithWrongEntryPointTemplate_DataConvertFailedExceptionShouldBeThrown(string entryPointTemplateName)
        {
            var request = GetHl7V2RequestWithEntryPointTemplate(entryPointTemplateName);
            var dataConvertEngine = GetDataConvertEngine();
            await Assert.ThrowsAsync<DataConvertFailedException>(() => dataConvertEngine.Process(request, CancellationToken.None));
        }

        private DataConvertEngine GetDataConvertEngine()
        {
            var dataConvertConfig = new DataConvertConfiguration
            {
                Enabled = true,
                ProcessTimeoutThreshold = TimeSpan.FromSeconds(1),
            };

            var registry = new ContainerRegistryInfo
            {
                ContainerRegistryServer = "test.azurecr.io",
            };

            dataConvertConfig.ContainerRegistries.Add(registry);

            IOptions<DataConvertConfiguration> dataConvertConfiguration = Substitute.For<IOptions<DataConvertConfiguration>>();
            dataConvertConfiguration.Value.Returns(dataConvertConfig);

            var tokenProvider = new ContainerRegistryBasicTokenProvider(dataConvertConfiguration);
            IOptionsMonitor<TemplateContainerConfig> templateConfig = Substitute.For<IOptionsMonitor<TemplateContainerConfig>>();
            templateConfig.CurrentValue.Returns(new TemplateContainerConfig());

            ITemplateProviderFactory templateProviderFactory = new TemplateProviderFactory(templateConfig, new NullLogger<TemplateProviderFactory>());

            return new DataConvertEngine(
                tokenProvider,
                templateProviderFactory,
                new NullLogger<DataConvertEngine>());
        }

        private static DataConvertRequest GetHl7V2RequestWithDefaultTemplates()
        {
            return new DataConvertRequest(GetSampleHl7v2Message(), DataConvertInputDataType.Hl7v2, DefaultTemplateReference, "ADT_A01");
        }

        private static DataConvertRequest GetHl7V2RequestWithTemplateReference(string imageReference)
        {
            return new DataConvertRequest(GetSampleHl7v2Message(), DataConvertInputDataType.Hl7v2, imageReference, "ADT_A01");
        }

        private static DataConvertRequest GetHl7V2RequestWithInputData(string inputData)
        {
            return new DataConvertRequest(inputData, DataConvertInputDataType.Hl7v2, DefaultTemplateReference, "ADT_A01");
        }

        private static DataConvertRequest GetHl7V2RequestWithEntryPointTemplate(string entryPointTemplate)
        {
            return new DataConvertRequest(GetSampleHl7v2Message(), DataConvertInputDataType.Hl7v2, DefaultTemplateReference, entryPointTemplate);
        }

        private static ImageInfo GetDefaultImageInfo()
        {
            return new ImageInfo("MicrosoftHealth", "FhirConverter", "default");
        }

        private static bool IsDefaultTemplate(ImageInfo imageInfo)
        {
            return string.Equals("MicrosoftHealth", imageInfo.Registry, StringComparison.OrdinalIgnoreCase)
                && string.Equals("FhirConverter", imageInfo.ImageName, StringComparison.OrdinalIgnoreCase)
                && string.Equals("default", imageInfo.Tag, StringComparison.OrdinalIgnoreCase);
        }

        private static string GetSampleHl7v2Message()
        {
            return "MSH|^~\\&|SIMHOSP|SFAC|RAPP|RFAC|20200508131015||ADT^A01|517|T|2.3|||AL||44|ASCII\nEVN|A01|20200508131015|||C005^Whittingham^Sylvia^^^Dr^^^DRNBR^PRSNL^^^ORGDR|\nPID|1|3735064194^^^SIMULATOR MRN^MRN|3735064194^^^SIMULATOR MRN^MRN~2021051528^^^NHSNBR^NHSNMBR||Kinmonth^Joanna^Chelsea^^Ms^^CURRENT||19870624000000|F|||89 Transaction House^Handmaiden Street^Wembley^^FV75 4GJ^GBR^HOME||020 3614 5541^HOME|||||||||C^White - Other^^^||||||||\nPD1|||FAMILY PRACTICE^^12345|\nPV1|1|I|OtherWard^MainRoom^Bed 183^Simulated Hospital^^BED^Main Building^4|28b|||C005^Whittingham^Sylvia^^^Dr^^^DRNBR^PRSNL^^^ORGDR|||CAR|||||||||16094728916771313876^^^^visitid||||||||||||||||||||||ARRIVED|||20200508131015||";
        }
    }
}
