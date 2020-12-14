// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations.ConvertData;
using Microsoft.Health.Fhir.Core.Features.Operations.ConvertData.Models;
using Microsoft.Health.Fhir.Core.Messages.ConvertData;
using Microsoft.Health.Fhir.TemplateManagement.Models;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Shared.Core.UnitTests.Features.Operations.ConvertData
{
    public class ContainerRegistryTemplateProviderTests
    {
        [Fact]
        public async Task GivenDefaultTemplateReference_WhenFetchingTemplates_DefaultTemplateCollectionShouldReturn()
        {
            var containerRegistryTemplateProvider = GetDefaultTemplateProvider();
            var templateReference = ImageInfo.DefaultTemplateImageReference;
            var templateCollection = await containerRegistryTemplateProvider.GetTemplateCollectionAsync(GetRequestWithTemplateReference(templateReference), CancellationToken.None);

            Assert.NotEmpty(templateCollection);
        }

        [Fact]
        public async Task GivenAnInvalidToken_WhenFetchingCustomTemplates_ExceptionShouldBeThrown()
        {
            var containerRegistryTemplateProvider = GetDefaultTemplateProvider();
            var templateReference = "test.azurecr.io/templates:latest";

            await Assert.ThrowsAsync<ContainerRegistryNotAuthorizedException>(() => containerRegistryTemplateProvider.GetTemplateCollectionAsync(GetRequestWithTemplateReference(templateReference), CancellationToken.None));
        }

        private IConvertDataTemplateProvider GetDefaultTemplateProvider()
        {
            IContainerRegistryTokenProvider tokenProvider = Substitute.For<IContainerRegistryTokenProvider>();
            tokenProvider.GetTokenAsync(default, default).ReturnsForAnyArgs("Bearer faketoken");

            var convertDataConfig = new ConvertDataConfiguration
            {
                Enabled = true,
                OperationTimeout = TimeSpan.FromSeconds(1),
            };
            convertDataConfig.ContainerRegistryServers.Add("test.azurecr.io");

            var config = Options.Create(convertDataConfig);
            return new ContainerRegistryTemplateProvider(tokenProvider, config, new NullLogger<ContainerRegistryTemplateProvider>());
        }

        private ConvertDataRequest GetRequestWithTemplateReference(string templateReference)
        {
            return new ConvertDataRequest(GetSampleHl7v2Message(), ConversionInputDataType.Hl7v2, templateReference.Split('/')[0], true, templateReference, "ADT_A01");
        }

        private static string GetSampleHl7v2Message()
        {
            return "MSH|^~\\&|SIMHOSP|SFAC|RAPP|RFAC|20200508131015||ADT^A01|517|T|2.3|||AL||44|ASCII\nEVN|A01|20200508131015|||C005^Whittingham^Sylvia^^^Dr^^^DRNBR^PRSNL^^^ORGDR|\nPID|1|3735064194^^^SIMULATOR MRN^MRN|3735064194^^^SIMULATOR MRN^MRN~2021051528^^^NHSNBR^NHSNMBR||Kinmonth^Joanna^Chelsea^^Ms^^CURRENT||19870624000000|F|||89 Transaction House^Handmaiden Street^Wembley^^FV75 4GJ^GBR^HOME||020 3614 5541^HOME|||||||||C^White - Other^^^||||||||\nPD1|||FAMILY PRACTICE^^12345|\nPV1|1|I|OtherWard^MainRoom^Bed 183^Simulated Hospital^^BED^Main Building^4|28b|||C005^Whittingham^Sylvia^^^Dr^^^DRNBR^PRSNL^^^ORGDR|||CAR|||||||||16094728916771313876^^^^visitid||||||||||||||||||||||ARRIVED|||20200508131015||";
        }
    }
}
