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
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations.DataConvert;
using Microsoft.Health.Fhir.Core.Features.Operations.DataConvert.Models;
using Microsoft.Health.Fhir.Core.Messages.DataConvert;
using Microsoft.Health.Fhir.TemplateManagement.Models;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Shared.Core.UnitTests.Features.Operations.DataConvert
{
    public class DataConvertEngineTests
    {
        private IDataConvertEngine _dataConvertEngine;

        public DataConvertEngineTests()
        {
            var dataConvertConfig = new DataConvertConfiguration
            {
                Enabled = true,
                OperationTimeout = TimeSpan.FromMilliseconds(50),
            };

            var registry = new ContainerRegistryInfo
            {
                ContainerRegistryServer = "test.azurecr.io",
            };
            dataConvertConfig.ContainerRegistries.Add(registry);

            IOptions<DataConvertConfiguration> dataConvertConfiguration = Options.Create(dataConvertConfig);

            IContainerRegistryTokenProvider tokenProvider = Substitute.For<IContainerRegistryTokenProvider>();
            tokenProvider.GetTokenAsync(Arg.Any<string>(), default).ReturnsForAnyArgs(x => GetToken(x[0].ToString(), dataConvertConfig));

            ContainerRegistryTemplateProvider templateProvider = new ContainerRegistryTemplateProvider(tokenProvider, dataConvertConfiguration, new NullLogger<ContainerRegistryTemplateProvider>());

            _dataConvertEngine = new DataConvertEngine(
                templateProvider,
                dataConvertConfiguration,
                new NullLogger<DataConvertEngine>());
        }

        [Fact]
        public async Task GivenDataConvertRequest_WithDefaultTemplates_CorrectResultShouldReturn()
        {
            var request = GetHl7V2RequestWithDefaultTemplates();
            var response = await _dataConvertEngine.Process(request, CancellationToken.None);

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

        [Theory]
        [InlineData("fakeacr.azurecr.io/template:default")]
        [InlineData("test.azurecr-test.io/template:default")]
        [InlineData("test.azurecr.com/template@sha256:592535ef52d742f81e35f4d87b43d9b535ed56cf58c90a14fc5fd7ea0fbb8696")]
        [InlineData("*****####.com/template:default")]
        [InlineData("¶Š™œãý£¾.com/template:default")]
        public async Task GivenDataConvertRequest_WithUnregisteredRegistry_ContainerRegistryNotConfiguredExceptionShouldBeThrown(string templateReference)
        {
            var request = GetHl7V2RequestWithTemplateReference(templateReference);
            await Assert.ThrowsAsync<ContainerRegistryNotConfiguredException>(() => _dataConvertEngine.Process(request, CancellationToken.None));
        }

        [Theory]
        [InlineData("       ")]
        [InlineData("Abc")]
        [InlineData("¶Š™œãý£¾")]
        [InlineData("MSH|")]
        public async Task GivenDataConvertRequest_WithWrongInputData_DataConvertFailedExceptionShouldBeThrown(string inputData)
        {
            var request = GetHl7V2RequestWithInputData(inputData);
            await Assert.ThrowsAsync<InputDataParseErrorException>(() => _dataConvertEngine.Process(request, CancellationToken.None));
        }

        [Theory]
        [InlineData("       ")]
        [InlineData("ADT_A02")]
        [InlineData("¶Š™œãý£¾")]
        public async Task GivenDataConvertRequest_WithWrongEntryPointTemplate_DataConvertFailedExceptionShouldBeThrown(string entryPointTemplateName)
        {
            var request = GetHl7V2RequestWithEntryPointTemplate(entryPointTemplateName);
            await Assert.ThrowsAsync<DataConvertFailedException>(() => _dataConvertEngine.Process(request, CancellationToken.None));
        }

        private static DataConvertRequest GetHl7V2RequestWithDefaultTemplates()
        {
            return new DataConvertRequest(GetSampleHl7v2Message(), DataConvertInputDataType.Hl7v2, "microsofthealth", ImageInfo.DefaultTemplateImageReference, "ADT_A01");
        }

        private static DataConvertRequest GetHl7V2RequestWithTemplateReference(string imageReference)
        {
            return new DataConvertRequest(GetSampleHl7v2Message(), DataConvertInputDataType.Hl7v2, imageReference.Split('/')[0], imageReference, "ADT_A01");
        }

        private static DataConvertRequest GetHl7V2RequestWithInputData(string inputData)
        {
            return new DataConvertRequest(inputData, DataConvertInputDataType.Hl7v2, "microsofthealth", ImageInfo.DefaultTemplateImageReference, "ADT_A01");
        }

        private static DataConvertRequest GetHl7V2RequestWithEntryPointTemplate(string entryPointTemplate)
        {
            return new DataConvertRequest(GetSampleHl7v2Message(), DataConvertInputDataType.Hl7v2, "microsofthealth", ImageInfo.DefaultTemplateImageReference, entryPointTemplate);
        }

        private static string GetSampleHl7v2Message()
        {
            return "MSH|^~\\&|SIMHOSP|SFAC|RAPP|RFAC|20200508131015||ADT^A01|517|T|2.3|||AL||44|ASCII\nEVN|A01|20200508131015|||C005^Whittingham^Sylvia^^^Dr^^^DRNBR^PRSNL^^^ORGDR|\nPID|1|3735064194^^^SIMULATOR MRN^MRN|3735064194^^^SIMULATOR MRN^MRN~2021051528^^^NHSNBR^NHSNMBR||Kinmonth^Joanna^Chelsea^^Ms^^CURRENT||19870624000000|F|||89 Transaction House^Handmaiden Street^Wembley^^FV75 4GJ^GBR^HOME||020 3614 5541^HOME|||||||||C^White - Other^^^||||||||\nPD1|||FAMILY PRACTICE^^12345|\nPV1|1|I|OtherWard^MainRoom^Bed 183^Simulated Hospital^^BED^Main Building^4|28b|||C005^Whittingham^Sylvia^^^Dr^^^DRNBR^PRSNL^^^ORGDR|||CAR|||||||||16094728916771313876^^^^visitid||||||||||||||||||||||ARRIVED|||20200508131015||";
        }

        // For unit tests, we only use the built-in templates and here returns an empty token.
        private string GetToken(string registry, DataConvertConfiguration config)
        {
            if (!config.ContainerRegistries.Any(registryInfo => string.Equals(registryInfo.ContainerRegistryServer, registry, StringComparison.OrdinalIgnoreCase)))
            {
                throw new ContainerRegistryNotConfiguredException("Container registry not configured.");
            }

            return string.Empty;
        }
    }
}
