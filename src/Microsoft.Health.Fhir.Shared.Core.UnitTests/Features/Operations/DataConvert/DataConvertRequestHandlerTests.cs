// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations.DataConvert;
using Microsoft.Health.Fhir.Core.Features.Operations.DataConvert.Models;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Messages.DataConvert;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Shared.Core.UnitTests.Features.Operations.DataConvert
{
    public class DataConvertRequestHandlerTests
    {
        private readonly IDataConvertEngine _dataConvertEngine = Substitute.For<IDataConvertEngine>();
        private readonly IFhirAuthorizationService _authorizationService = Substitute.For<IFhirAuthorizationService>();
        private readonly DataConvertRequestHandler _dataConvertRequestHandler;

        public DataConvertRequestHandlerTests()
        {
            _dataConvertRequestHandler = GetRequestHandler();
        }

        [Fact]
        public async Task GivenAConvertTask_WhichRunsLongTime_TimeoutExceptionShouldBeThrown()
        {
            Task<DataConvertResponse> delayedResult = Task.Delay(2000).ContinueWith(_ => new DataConvertResponse("test"));
            _dataConvertEngine.Process(default, default).ReturnsForAnyArgs(delayedResult);

            await Assert.ThrowsAsync<DataConvertTimeoutException>(() => _dataConvertRequestHandler.Handle(GetSampleHl7v2Request(), default));
        }

        [Fact]
        public async Task GivenAConvertTask_WhichRunsFast_TimeoutExceptionShouldNotBeThrown()
        {
            Task<DataConvertResponse> delayedResult = Task.Delay(500).ContinueWith(_ => new DataConvertResponse("test"));
            _dataConvertEngine.Process(default, default).ReturnsForAnyArgs(delayedResult);

            await _dataConvertRequestHandler.Handle(GetSampleHl7v2Request(), default);
        }

        private DataConvertRequestHandler GetRequestHandler()
        {
            var datatConvertConfig = new DataConvertConfiguration
            {
                Enabled = true,
                ProcessTimeoutThreshold = TimeSpan.FromSeconds(1),
            };

            IOptions<DataConvertConfiguration> dataConvertConfiguration = Substitute.For<IOptions<DataConvertConfiguration>>();
            dataConvertConfiguration.Value.Returns(datatConvertConfig);
            _authorizationService.CheckAccess(default).ReturnsForAnyArgs(DataActions.DataConvert);
            return new DataConvertRequestHandler(
                _authorizationService,
                _dataConvertEngine,
                dataConvertConfiguration);
        }

        private static DataConvertRequest GetSampleHl7v2Request()
        {
            return new DataConvertRequest(GetSampleHl7v2Message(), DataConvertInputDataType.Hl7v2, "test.azurecr.io/testimage:latest", "ADT_A01");
        }

        private static string GetSampleHl7v2Message()
        {
            return "MSH|^~\\&|SIMHOSP|SFAC|RAPP|RFAC|20200508131015||ADT^A01|517|T|2.3|||AL||44|ASCII\nEVN|A01|20200508131015|||C005^Whittingham^Sylvia^^^Dr^^^DRNBR^PRSNL^^^ORGDR|\nPID|1|3735064194^^^SIMULATOR MRN^MRN|3735064194^^^SIMULATOR MRN^MRN~2021051528^^^NHSNBR^NHSNMBR||Kinmonth^Joanna^Chelsea^^Ms^^CURRENT||19870624000000|F|||89 Transaction House^Handmaiden Street^Wembley^^FV75 4GJ^GBR^HOME||020 3614 5541^HOME|||||||||C^White - Other^^^||||||||\nPD1|||FAMILY PRACTICE^^12345|\nPV1|1|I|OtherWard^MainRoom^Bed 183^Simulated Hospital^^BED^Main Building^4|28b|||C005^Whittingham^Sylvia^^^Dr^^^DRNBR^PRSNL^^^ORGDR|||CAR|||||||||16094728916771313876^^^^visitid||||||||||||||||||||||ARRIVED|||20200508131015||";
        }
    }
}
