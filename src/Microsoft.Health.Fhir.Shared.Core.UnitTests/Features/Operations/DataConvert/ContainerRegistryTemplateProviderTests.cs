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
using Microsoft.Health.Fhir.Core.Features.Operations.DataConvert;
using Microsoft.Health.Fhir.Core.Features.Operations.DataConvert.Models;
using Microsoft.Health.Fhir.Core.Messages.DataConvert;
using Microsoft.Health.Fhir.TemplateManagement.Models;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Shared.Core.UnitTests.Features.Operations.DataConvert
{
    public class ContainerRegistryTemplateProviderTests
    {
        private ContainerRegistryTemplateProvider _containerRegistryTemplateProvider;

        public ContainerRegistryTemplateProviderTests()
        {
            IContainerRegistryTokenProvider tokenProvider = Substitute.For<IContainerRegistryTokenProvider>();
            tokenProvider.GetTokenAsync(default, default).ReturnsForAnyArgs("Bearer faketoken");

            var dataConvertConfig = new DataConvertConfiguration
            {
                Enabled = true,
                OperationTimeout = TimeSpan.FromMilliseconds(50),
                ContainerRegistryTokenExpiration = TimeSpan.FromSeconds(1),
            };
            dataConvertConfig.ContainerRegistryServers.Add("test.azurecr.io");

            var config = Options.Create(dataConvertConfig);
            _containerRegistryTemplateProvider = new ContainerRegistryTemplateProvider(tokenProvider, config, new NullLogger<ContainerRegistryTemplateProvider>());
        }

        [Fact]
        public async Task GivenDefaultTemplateReference_WhenFetchingTemplates_DefaultTemplateCollectionShouldReturn()
        {
            var templateReference = ImageInfo.DefaultTemplateImageReference;
            var templateCollection = await _containerRegistryTemplateProvider.GetTemplateCollectionAsync(GetRequestWithTemplateReference(templateReference), CancellationToken.None);

            Assert.NotEmpty(templateCollection);
        }

        [Fact]
        public async Task GivenAnInvalidToken_WhenFetchingCustomTemplates_ExceptionShouldBeThrown()
        {
            var templateReference = "test.azurecr.io/templates:latest";
            await Assert.ThrowsAsync<FetchTemplateCollectionFailedException>(() => _containerRegistryTemplateProvider.GetTemplateCollectionAsync(GetRequestWithTemplateReference(templateReference), CancellationToken.None));
        }

        private DataConvertRequest GetRequestWithTemplateReference(string templateReference)
        {
            return new DataConvertRequest(GetSampleHl7v2Message(), DataConvertInputDataType.Hl7v2, templateReference.Split('/')[0], templateReference, "ADT_A01");
        }

        private static string GetSampleHl7v2Message()
        {
            return "MSH|^~\\&|SIMHOSP|SFAC|RAPP|RFAC|20200508131015||ADT^A01|517|T|2.3|||AL||44|ASCII\nEVN|A01|20200508131015|||C005^Whittingham^Sylvia^^^Dr^^^DRNBR^PRSNL^^^ORGDR|\nPID|1|3735064194^^^SIMULATOR MRN^MRN|3735064194^^^SIMULATOR MRN^MRN~2021051528^^^NHSNBR^NHSNMBR||Kinmonth^Joanna^Chelsea^^Ms^^CURRENT||19870624000000|F|||89 Transaction House^Handmaiden Street^Wembley^^FV75 4GJ^GBR^HOME||020 3614 5541^HOME|||||||||C^White - Other^^^||||||||\nPD1|||FAMILY PRACTICE^^12345|\nPV1|1|I|OtherWard^MainRoom^Bed 183^Simulated Hospital^^BED^Main Building^4|28b|||C005^Whittingham^Sylvia^^^Dr^^^DRNBR^PRSNL^^^ORGDR|||CAR|||||||||16094728916771313876^^^^visitid||||||||||||||||||||||ARRIVED|||20200508131015||";
        }
    }
}
