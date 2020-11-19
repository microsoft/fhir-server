// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations.DataConvert;
using Microsoft.Health.Fhir.Core.Features.Operations.DataConvert.Models;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Messages.DataConvert;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Shared.Core.UnitTests.Features.Operations.DataConvert
{
    public class DataConvertRequestHandlerTests
    {
        private readonly object balanceLock = new object();

        [Fact]
        public async Task GivenAConvertRequest_WhenDataConvert_CorrectResponseShouldReturn()
        {
            var dataConvertRequestHandler = GetRequestHandler();
            var response = await dataConvertRequestHandler.Handle(GetSampleHl7v2Request(), default);

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

        private DataConvertRequestHandler GetRequestHandler(int milliseconds = 5000)
        {
            var dataConvertConfig = new DataConvertConfiguration
            {
                Enabled = true,
                OperationTimeout = TimeSpan.FromMilliseconds(milliseconds),
            };
            dataConvertConfig.ContainerRegistryServers.Add("test.azurecr.io");

            IOptions<DataConvertConfiguration> dataConvertConfiguration = Options.Create(dataConvertConfig);

            IContainerRegistryTokenProvider tokenProvider = Substitute.For<IContainerRegistryTokenProvider>();
            tokenProvider.GetTokenAsync(Arg.Any<string>(), default).ReturnsForAnyArgs(string.Empty);

            ContainerRegistryTemplateProvider templateProvider = new ContainerRegistryTemplateProvider(tokenProvider, dataConvertConfiguration, new NullLogger<ContainerRegistryTemplateProvider>());

            var dataConvertEngine = new DataConvertEngine(
                templateProvider,
                dataConvertConfiguration,
                new NullLogger<DataConvertEngine>());

            IFhirAuthorizationService authorizationService = Substitute.For<IFhirAuthorizationService>();
            authorizationService.CheckAccess(default).ReturnsForAnyArgs(DataActions.DataConvert);

            return new DataConvertRequestHandler(
                authorizationService,
                dataConvertEngine,
                dataConvertConfiguration);
        }

        private static DataConvertRequest GetSampleHl7v2Request()
        {
            return new DataConvertRequest(GetSampleHl7v2Message(), DataConvertInputDataType.Hl7v2, "microsofthealth", "microsofthealth/fhirconverter:default", "ADT_A01");
        }

        private static string GetSampleHl7v2Message()
        {
            return "MSH|^~\\&|SIMHOSP|SFAC|RAPP|RFAC|20200508131015||ADT^A01|517|T|2.3|||AL||44|ASCII\nEVN|A01|20200508131015|||C005^Whittingham^Sylvia^^^Dr^^^DRNBR^PRSNL^^^ORGDR|\nPID|1|3735064194^^^SIMULATOR MRN^MRN|3735064194^^^SIMULATOR MRN^MRN~2021051528^^^NHSNBR^NHSNMBR||Kinmonth^Joanna^Chelsea^^Ms^^CURRENT||19870624000000|F|||89 Transaction House^Handmaiden Street^Wembley^^FV75 4GJ^GBR^HOME||020 3614 5541^HOME|||||||||C^White - Other^^^||||||||\nPD1|||FAMILY PRACTICE^^12345|\nPV1|1|I|OtherWard^MainRoom^Bed 183^Simulated Hospital^^BED^Main Building^4|28b|||C005^Whittingham^Sylvia^^^Dr^^^DRNBR^PRSNL^^^ORGDR|||CAR|||||||||16094728916771313876^^^^visitid||||||||||||||||||||||ARRIVED|||20200508131015||";
        }
    }
}
