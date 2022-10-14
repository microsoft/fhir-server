// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Threading;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.Api.Controllers;
using Microsoft.Health.Fhir.Api.Features.ActionResults;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Messages.MemberMatch;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Api.UnitTests.Controllers
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.MemberMatch)]
    public class MemberMatchControllerTests
    {
        private MemberMatchController _memberMatchController;
        private IMediator _mediator = Substitute.For<IMediator>();

        public MemberMatchControllerTests()
        {
            _memberMatchController = new MemberMatchController(
                _mediator,
                NullLogger<MemberMatchController>.Instance);
            _memberMatchController.ControllerContext = new ControllerContext();
            _memberMatchController.ControllerContext.HttpContext = new DefaultHttpContext();
        }

        public static TheoryData<Parameters> InvalidBody =>
            new TheoryData<Parameters>
            {
                GetParamsResourceWithMissingParams(),
                GetParamsResourceWithWrongNameParam(),
                GetParamsResourceWithWrongContent(),
            };

        public static TheoryData<Parameters> ValidBody =>
            new TheoryData<Parameters>
            {
                GetValidBody(),
            };

        [Theory]
        [MemberData(nameof(InvalidBody), MemberType = typeof(MemberMatchControllerTests))]
        public async Task GivenAMemberMatchDataRequest_WhenInvalidBodySent_ThenRequestNotValidThrown(Parameters body)
        {
            await Assert.ThrowsAsync<RequestNotValidException>(() => _memberMatchController.MemberMatch(body));
        }

        [Theory]
        [MemberData(nameof(ValidBody), MemberType = typeof(MemberMatchControllerTests))]
        public async Task GivenAMemberMatchDataRequest_WithValidBody_ThenMemberMatchCalledWithCorrectParams(Parameters body)
        {
            _mediator.Send<MemberMatchResponse>(Arg.Any<MemberMatchRequest>(), Arg.Any<CancellationToken>()).Returns(GetMemberMatchResponse());
            var result = await _memberMatchController.MemberMatch(body) as MemberMatchResult;
            Assert.NotNull(result);
            Assert.Equal(System.Net.HttpStatusCode.OK, result.StatusCode);
        }

        private static MemberMatchResponse GetMemberMatchResponse() => new MemberMatchResponse(Samples.GetDefaultPatient());

        private static Parameters GetParamsResourceWithWrongNameParam()
        {
            var parametersResource = new Parameters();
            parametersResource.Parameter = new List<Parameters.ParameterComponent>();

            AddParamComponent(parametersResource, MemberMatchController.Patient, string.Empty);
            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = "foo", Value = new FhirDecimal(5) });

            return parametersResource;
        }

        private static Parameters GetParamsResourceWithMissingParams()
        {
            var parametersResource = new Parameters();
            parametersResource.Parameter = new List<Parameters.ParameterComponent>();

            AddParamComponent(parametersResource, MemberMatchController.Coverage, string.Empty);

            return parametersResource;
        }

        private static Parameters GetParamsResourceWithWrongContent()
        {
            var parametersResource = new Parameters();
            parametersResource.Parameter = new List<Parameters.ParameterComponent>();

            AddParamComponent(parametersResource, MemberMatchController.Coverage, string.Empty);
            AddParamComponent(parametersResource, MemberMatchController.Patient, string.Empty);
            return parametersResource;
        }

        private static Parameters GetValidBody()
        {
            var parametersResource = new Parameters();
            parametersResource.Add(MemberMatchController.Patient, Samples.GetDefaultPatient().ToPoco<Patient>());
            parametersResource.Add(MemberMatchController.Coverage, Samples.GetDefaultCoverage().ToPoco<Coverage>());

            return parametersResource;
        }

        private static void AddParamComponent(Parameters resource, string name, string value) =>
            resource.Parameter.Add(new Parameters.ParameterComponent() { Name = name, Value = new FhirString(value) });
    }
}
