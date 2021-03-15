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
using Microsoft.Health.Fhir.Core.Messages.ConvertData;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Shared.Api.UnitTests.Controllers
{
    public class ConvertDataControllerTests
    {
        private ConvertDataController _convertDataeEnabledController;
        private IMediator _mediator = Substitute.For<IMediator>();
        private HttpContext _httpContext = new DefaultHttpContext();
        private static ConvertDataConfiguration _convertDataJobConfig = new ConvertDataConfiguration() { Enabled = true };

        public ConvertDataControllerTests()
        {
            _convertDataJobConfig.ContainerRegistryServers.Add("test.azurecr.io");
            _convertDataeEnabledController = GetController(_convertDataJobConfig);
            var controllerContext = new ControllerContext() { HttpContext = _httpContext };
            _convertDataeEnabledController.ControllerContext = controllerContext;
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
                GetValidConvertDataParams(),
            };

        [Theory]
        [MemberData(nameof(InvalidBody), MemberType = typeof(ConvertDataControllerTests))]
        public async Task GivenAConvertDataRequest_WhenInvalidBodySent_ThenRequestNotValidThrown(Parameters body)
        {
            _convertDataeEnabledController.ControllerContext.HttpContext.Request.Method = HttpMethods.Post;
            await Assert.ThrowsAsync<RequestNotValidException>(() => _convertDataeEnabledController.ConvertData(body));
        }

        [Theory]
        [InlineData("abc.azurecr.io")]
        [InlineData("abc.azurecr.io/:tag")]
        [InlineData("testimage:tag")]
        public async Task GivenAConvertDataRequest_WithInvalidReference_WhenInvalidBodySent_ThenRequestNotValidThrown(string templateCollectionReference)
        {
            var body = GetConvertDataParamsWithCustomTemplateReference(templateCollectionReference);

            _convertDataeEnabledController.ControllerContext.HttpContext.Request.Method = HttpMethods.Post;
            await Assert.ThrowsAsync<RequestNotValidException>(() => _convertDataeEnabledController.ConvertData(body));
        }

        [Theory]
        [MemberData(nameof(ValidBody), MemberType = typeof(ConvertDataControllerTests))]
        public async Task GivenAConvertDataRequest_WithValidBody_ThenConvertDataCalledWithCorrectParams(Parameters body)
        {
            _convertDataeEnabledController.ControllerContext.HttpContext.Request.Method = HttpMethods.Post;
            _mediator.Send(Arg.Any<ConvertDataRequest>()).Returns(Task.FromResult(GetConvertDataResponse()));
            await _convertDataeEnabledController.ConvertData(body);
            await _mediator.Received().Send(
                Arg.Is<ConvertDataRequest>(
                    r => r.InputData.ToString().Equals(body.Parameter.Find(p => p.Name.Equals(ConvertDataProperties.InputData)).Value.ToString())
                && r.InputDataType.ToString() == body.Parameter.Find(p => p.Name.Equals(ConvertDataProperties.InputDataType)).Value.ToString()
                && r.TemplateCollectionReference == body.Parameter.Find(p => p.Name.Equals(ConvertDataProperties.TemplateCollectionReference)).Value.ToString()
                && r.RootTemplate == body.Parameter.Find(p => p.Name.Equals(ConvertDataProperties.RootTemplate)).Value.ToString()),
                Arg.Any<CancellationToken>());
            _mediator.ClearReceivedCalls();
        }

        private static ConvertDataResponse GetConvertDataResponse()
        {
            return new ConvertDataResponse(GetSampleConvertDataResponse());
        }

        private ConvertDataController GetController(ConvertDataConfiguration convertDataConfiguration)
        {
            var operationConfig = new OperationsConfiguration()
            {
                ConvertData = convertDataConfiguration,
            };

            IOptions<OperationsConfiguration> optionsOperationConfiguration = Substitute.For<IOptions<OperationsConfiguration>>();
            optionsOperationConfiguration.Value.Returns(operationConfig);

            return new ConvertDataController(
                _mediator,
                optionsOperationConfiguration,
                NullLogger<ConvertDataController>.Instance);
        }

        private static Parameters GetParamsResourceWithWrongNameParam()
        {
            var parametersResource = new Parameters();
            parametersResource.Parameter = new List<Parameters.ParameterComponent>();

            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = "foo", Value = new FhirDecimal(5) });
            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = ConvertDataProperties.InputData, Value = new FhirString(GetSampleHl7v2Message()) });

            return parametersResource;
        }

        private static Parameters GetParamsResourceWithMissingParams()
        {
            var parametersResource = new Parameters();
            parametersResource.Parameter = new List<Parameters.ParameterComponent>();

            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = ConvertDataProperties.InputData, Value = new FhirString(GetSampleHl7v2Message()) });
            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = ConvertDataProperties.InputDataType, Value = new FhirString("Hl7v2") });
            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = ConvertDataProperties.TemplateCollectionReference, Value = new FhirString("test.azurecr.io/testimage:latest") });

            return parametersResource;
        }

        private static Parameters GetParamsResourceWithTooManyParams()
        {
            var parametersResource = new Parameters();
            parametersResource.Parameter = new List<Parameters.ParameterComponent>();

            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = ConvertDataProperties.InputData, Value = new FhirString(GetSampleHl7v2Message()) });
            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = ConvertDataProperties.InputDataType, Value = new FhirString("Hl7v2") });
            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = ConvertDataProperties.TemplateCollectionReference, Value = new FhirString("test.azurecr.io/testimage:latest") });
            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = ConvertDataProperties.RootTemplate, Value = new FhirString("ADT_A01") });
            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = "foo", Value = new FhirDecimal(5) });

            return parametersResource;
        }

        private static Parameters GetValidConvertDataParams()
        {
            var parametersResource = new Parameters();
            parametersResource.Parameter = new List<Parameters.ParameterComponent>();

            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = ConvertDataProperties.InputData, Value = new FhirString(GetSampleHl7v2Message()) });
            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = ConvertDataProperties.InputDataType, Value = new FhirString("Hl7v2") });
            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = ConvertDataProperties.TemplateCollectionReference, Value = new FhirString("test.azurecr.io/testimage:latest") });
            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = ConvertDataProperties.RootTemplate, Value = new FhirString("ADT_A01") });

            return parametersResource;
        }

        private static Parameters GetConvertDataParamsWithCustomTemplateReference(string templateCollectionReference)
        {
            var parametersResource = new Parameters();
            parametersResource.Parameter = new List<Parameters.ParameterComponent>();

            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = ConvertDataProperties.InputData, Value = new FhirString(GetSampleHl7v2Message()) });
            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = ConvertDataProperties.InputDataType, Value = new FhirString("Hl7v2") });
            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = ConvertDataProperties.TemplateCollectionReference, Value = new FhirString(templateCollectionReference) });
            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = ConvertDataProperties.RootTemplate, Value = new FhirString("ADT_A01") });

            return parametersResource;
        }

        private static string GetSampleHl7v2Message()
        {
            return "MSH|^~\\&|AccMgr|1|||20050110045504||ADT^A01|599102|P|2.3||| \nEVN|A01|20050110045502||||| \nPID|1||10006579^^^1^MR^1||DUCK^DONALD^D||19241010|M||1|111 DUCK ST^^FOWL^CA^999990000^^M|1|8885551212|8885551212|1|2||40007716^^^AccMgr^VN^1|123121234|||||||||||NO \nNK1|1|DUCK^HUEY|SO|3583 DUCK RD^^FOWL^CA^999990000|8885552222||Y|||||||||||||| \nPV1|1|I|PREOP^101^1^1^^^S|3|||37^DISNEY^WALT^^^^^^AccMgr^^^^CI|||01||||1|||37^DISNEY^WALT^^^^^^AccMgr^^^^CI|2|40007716^^^AccMgr^VN|4|||||||||||||||||||1||G|||20050110045253|||||| \nGT1|1|8291|DUCK^DONALD^D||111^DUCK ST^^FOWL^CA^999990000|8885551212||19241010|M||1|123121234||||#Cartoon Ducks Inc|111^DUCK ST^^FOWL^CA^999990000|8885551212||PT| \nDG1|1|I9|71596^OSTEOARTHROS NOS-L/LEG ^I9|OSTEOARTHROS NOS-L/LEG ||A| \nIN1|1|MEDICARE|3|MEDICARE|||||||Cartoon Ducks Inc|19891001|||4|DUCK^DONALD^D|1|19241010|111^DUCK ST^^FOWL^CA^999990000|||||||||||||||||123121234A||||||PT|M|111 DUCK ST^^FOWL^CA^999990000|||||8291 \nIN2|1||123121234|Cartoon Ducks Inc|||123121234A|||||||||||||||||||||||||||||||||||||||||||||||||||||||||8885551212 \nIN1|2|NON-PRIMARY|9|MEDICAL MUTUAL CALIF.|PO BOX 94776^^HOLLYWOOD^CA^441414776||8003621279|PUBSUMB|||Cartoon Ducks Inc||||7|DUCK^DONALD^D|1|19241010|111 DUCK ST^^FOWL^CA^999990000|||||||||||||||||056269770||||||PT|M|111^DUCK ST^^FOWL^CA^999990000|||||8291 \nIN2|2||123121234|Cartoon Ducks Inc||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||8885551212 \nIN1|3|SELF PAY|1|SELF PAY|||||||||||5||1\n";
        }

        private static string GetSampleConvertDataResponse()
        {
            return "{ \"resourceType\": \"Bundle\" }";
        }
    }
}
