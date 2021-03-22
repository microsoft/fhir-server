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
using Microsoft.Health.Fhir.Core.Features.Operations.ConvertData;
using Microsoft.Health.Fhir.Core.Features.Operations.ConvertData.Models;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Messages.ConvertData;
using Microsoft.Health.Fhir.TemplateManagement.Models;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Shared.Core.UnitTests.Features.Operations.ConvertData
{
    public class ConvertDataRequestHandlerTests
    {
        [Fact]
        public async Task GivenAConvertRequest_WhenConvertData_CorrectResponseShouldReturn()
        {
            var convertDataRequestHandler = GetRequestHandler();
            var response = await convertDataRequestHandler.Handle(GetSampleHl7v2Request(), default);

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

        private ConvertDataRequestHandler GetRequestHandler()
        {
            var convertDataConfig = new ConvertDataConfiguration
            {
                Enabled = true,
                OperationTimeout = TimeSpan.FromSeconds(1),
            };
            convertDataConfig.ContainerRegistryServers.Add("test.azurecr.io");

            IOptions<ConvertDataConfiguration> convertDataConfiguration = Options.Create(convertDataConfig);

            IContainerRegistryTokenProvider tokenProvider = Substitute.For<IContainerRegistryTokenProvider>();
            tokenProvider.GetTokenAsync(Arg.Any<string>(), default).ReturnsForAnyArgs(string.Empty);

            ContainerRegistryTemplateProvider templateProvider = new ContainerRegistryTemplateProvider(tokenProvider, convertDataConfiguration, new NullLogger<ContainerRegistryTemplateProvider>());

            var convertDataEngine = new ConvertDataEngine(
                templateProvider,
                convertDataConfiguration,
                new NullLogger<ConvertDataEngine>());

            IFhirAuthorizationService authorizationService = Substitute.For<IFhirAuthorizationService>();
            authorizationService.CheckAccess(default).ReturnsForAnyArgs(DataActions.ConvertData);

            return new ConvertDataRequestHandler(
                authorizationService,
                convertDataEngine,
                convertDataConfiguration);
        }

        private static ConvertDataRequest GetSampleHl7v2Request()
        {
            return new ConvertDataRequest(GetSampleHl7v2Message(), ConversionInputDataType.Hl7v2, "microsofthealth", true, ImageInfo.DefaultTemplateImageReference, "ADT_A01");
        }

        private static string GetSampleHl7v2Message()
        {
            return "MSH|^~\\&|AccMgr|1|||20050110045504||ADT^A01|599102|P|2.3||| \nEVN|A01|20050110045502||||| \nPID|1||10006579^^^1^MR^1||DUCK^DONALD^D||19241010|M||1|111 DUCK ST^^FOWL^CA^999990000^^M|1|8885551212|8885551212|1|2||40007716^^^AccMgr^VN^1|123121234|||||||||||NO \nNK1|1|DUCK^HUEY|SO|3583 DUCK RD^^FOWL^CA^999990000|8885552222||Y|||||||||||||| \nPV1|1|I|PREOP^101^1^1^^^S|3|||37^DISNEY^WALT^^^^^^AccMgr^^^^CI|||01||||1|||37^DISNEY^WALT^^^^^^AccMgr^^^^CI|2|40007716^^^AccMgr^VN|4|||||||||||||||||||1||G|||20050110045253|||||| \nGT1|1|8291|DUCK^DONALD^D||111^DUCK ST^^FOWL^CA^999990000|8885551212||19241010|M||1|123121234||||#Cartoon Ducks Inc|111^DUCK ST^^FOWL^CA^999990000|8885551212||PT| \nDG1|1|I9|71596^OSTEOARTHROS NOS-L/LEG ^I9|OSTEOARTHROS NOS-L/LEG ||A| \nIN1|1|MEDICARE|3|MEDICARE|||||||Cartoon Ducks Inc|19891001|||4|DUCK^DONALD^D|1|19241010|111^DUCK ST^^FOWL^CA^999990000|||||||||||||||||123121234A||||||PT|M|111 DUCK ST^^FOWL^CA^999990000|||||8291 \nIN2|1||123121234|Cartoon Ducks Inc|||123121234A|||||||||||||||||||||||||||||||||||||||||||||||||||||||||8885551212 \nIN1|2|NON-PRIMARY|9|MEDICAL MUTUAL CALIF.|PO BOX 94776^^HOLLYWOOD^CA^441414776||8003621279|PUBSUMB|||Cartoon Ducks Inc||||7|DUCK^DONALD^D|1|19241010|111 DUCK ST^^FOWL^CA^999990000|||||||||||||||||056269770||||||PT|M|111^DUCK ST^^FOWL^CA^999990000|||||8291 \nIN2|2||123121234|Cartoon Ducks Inc||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||8885551212 \nIN1|3|SELF PAY|1|SELF PAY|||||||||||5||1\n";
        }
    }
}
