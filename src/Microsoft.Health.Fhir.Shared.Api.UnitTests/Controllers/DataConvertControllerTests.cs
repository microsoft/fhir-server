// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Threading;
using Hl7.Fhir.Model;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Api.Controllers;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Messages.DataConvert;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Shared.Api.UnitTests.Controllers
{
    public class DataConvertControllerTests
    {
        private DataConvertController _dataConverteEnabledController;
        private IMediator _mediator = Substitute.For<IMediator>();
        private HttpContext _httpContext = new DefaultHttpContext();
        private static DataConvertConfiguration _dataConvertJobConfig = new DataConvertConfiguration() { Enabled = true };

        public DataConvertControllerTests()
        {
            _dataConverteEnabledController = GetController(_dataConvertJobConfig);
            var controllerContext = new ControllerContext() { HttpContext = _httpContext };
            _dataConverteEnabledController.ControllerContext = controllerContext;
        }

        public static TheoryData<Parameters> InvalidBody =>
            new TheoryData<Parameters>
            {
                GetParamsResourceWithTooManyParams(),
                GetParamsResourceWithMissingParams(),
                GetParamsResourceWithWrongNameParam(),
                null,
            };

        public static TheoryData<Parameters> ValidBody =>
            new TheoryData<Parameters>
            {
                GetValidDataConvertParams(),
            };

        [Theory]
        [MemberData(nameof(InvalidBody), MemberType = typeof(DataConvertControllerTests))]
        public async Task GivenADataConvertRequest_WhenInvalidBodySent_ThenRequestNotValidThrown(Parameters body)
        {
            _dataConverteEnabledController.ControllerContext.HttpContext.Request.Method = HttpMethods.Post;
            await Assert.ThrowsAsync<RequestNotValidException>(() => _dataConverteEnabledController.DataConvert(body));
        }

        [Theory]
        [MemberData(nameof(ValidBody), MemberType = typeof(DataConvertControllerTests))]
        public async Task GivenADataConvertRequest_WithValidBody_ThenDataConvertCalledWithCorrectParams(Parameters body)
        {
            _dataConverteEnabledController.ControllerContext.HttpContext.Request.Method = HttpMethods.Post;
            _mediator.Send(Arg.Any<DataConvertRequest>()).Returns(Task.FromResult(GetDataConvertResponse()));
            await _dataConverteEnabledController.DataConvert(body);
            await _mediator.Received().Send(
                Arg.Is<DataConvertRequest>(
                    r => r.InputData.ToString().Equals(body.Parameter.Find(p => p.Name.Equals(JobRecordProperties.InputData)).Value.ToString())
                && r.InputDataType.ToString() == body.Parameter.Find(p => p.Name.Equals(JobRecordProperties.InputDataType)).Value.ToString()
                && r.TemplateSetReference == body.Parameter.Find(p => p.Name.Equals(JobRecordProperties.TemplateSetReference)).Value.ToString()
                && r.EntryPointTemplate == body.Parameter.Find(p => p.Name.Equals(JobRecordProperties.EntryPointTemplate)).Value.ToString()),
                Arg.Any<CancellationToken>());
            _mediator.ClearReceivedCalls();
        }

        private static DataConvertResponse GetDataConvertResponse()
        {
            return new DataConvertResponse(GetSampleDataConvertResponse());
        }

        private DataConvertController GetController(DataConvertConfiguration dataConvertConfiguration)
        {
            var operationConfig = new OperationsConfiguration()
            {
                DataConvert = dataConvertConfiguration,
            };

            IOptions<OperationsConfiguration> optionsOperationConfiguration = Substitute.For<IOptions<OperationsConfiguration>>();
            optionsOperationConfiguration.Value.Returns(operationConfig);

            return new DataConvertController(
                _mediator,
                optionsOperationConfiguration,
                NullLogger<DataConvertController>.Instance);
        }

        private static Parameters GetParamsResourceWithWrongNameParam()
        {
            var parametersResource = new Parameters();
            parametersResource.Parameter = new List<Parameters.ParameterComponent>();

            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = "foo", Value = new FhirDecimal(5) });
            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = JobRecordProperties.InputData, Value = new FhirString(GetSampleHl7v2Message()) });

            return parametersResource;
        }

        private static Parameters GetParamsResourceWithMissingParams()
        {
            var parametersResource = new Parameters();
            parametersResource.Parameter = new List<Parameters.ParameterComponent>();

            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = JobRecordProperties.InputData, Value = new FhirString(GetSampleHl7v2Message()) });
            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = JobRecordProperties.InputDataType, Value = new FhirString("Hl7v2") });
            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = JobRecordProperties.TemplateSetReference, Value = new FhirString("test.azurecr.io/testimage:latest") });

            return parametersResource;
        }

        private static Parameters GetParamsResourceWithTooManyParams()
        {
            var parametersResource = new Parameters();
            parametersResource.Parameter = new List<Parameters.ParameterComponent>();

            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = JobRecordProperties.InputData, Value = new FhirString(GetSampleHl7v2Message()) });
            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = JobRecordProperties.InputDataType, Value = new FhirString("Hl7v2") });
            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = JobRecordProperties.TemplateSetReference, Value = new FhirString("test.azurecr.io/testimage:latest") });
            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = JobRecordProperties.EntryPointTemplate, Value = new FhirString("ADT_A01") });
            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = "foo", Value = new FhirDecimal(5) });

            return parametersResource;
        }

        private static Parameters GetValidDataConvertParams()
        {
            var parametersResource = new Parameters();
            parametersResource.Parameter = new List<Parameters.ParameterComponent>();

            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = JobRecordProperties.InputData, Value = new FhirString(GetSampleHl7v2Message()) });
            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = JobRecordProperties.InputDataType, Value = new FhirString("Hl7v2") });
            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = JobRecordProperties.TemplateSetReference, Value = new FhirString("test.azurecr.io/testimage:latest") });
            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = JobRecordProperties.EntryPointTemplate, Value = new FhirString("ADT_A01") });

            return parametersResource;
        }

        private static string GetSampleHl7v2Message()
        {
            return "MSH|^~\\&|SIMHOSP|SFAC|RAPP|RFAC|20200508131015||ADT^A01|517|T|2.3|||AL||44|ASCII\nEVN|A01|20200508131015|||C005^Whittingham^Sylvia^^^Dr^^^DRNBR^PRSNL^^^ORGDR|\nPID|1|3735064194^^^SIMULATOR MRN^MRN|3735064194^^^SIMULATOR MRN^MRN~2021051528^^^NHSNBR^NHSNMBR||Kinmonth^Joanna^Chelsea^^Ms^^CURRENT||19870624000000|F|||89 Transaction House^Handmaiden Street^Wembley^^FV75 4GJ^GBR^HOME||020 3614 5541^HOME|||||||||C^White - Other^^^||||||||\nPD1|||FAMILY PRACTICE^^12345|\nPV1|1|I|OtherWard^MainRoom^Bed 183^Simulated Hospital^^BED^Main Building^4|28b|||C005^Whittingham^Sylvia^^^Dr^^^DRNBR^PRSNL^^^ORGDR|||CAR|||||||||16094728916771313876^^^^visitid||||||||||||||||||||||ARRIVED|||20200508131015||";
        }

        private static string GetSampleDataConvertResponse()
        {
            return "{ \"resourceType\": \"Bundle\" }";
        }
    }
}
