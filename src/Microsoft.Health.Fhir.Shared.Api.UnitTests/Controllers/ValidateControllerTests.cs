// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Health.Fhir.Api.Controllers;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Messages.Get;
using Microsoft.Health.Fhir.Core.Messages.Operation;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Api.UnitTests.Controllers
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Validate)]
    public class ValidateControllerTests
    {
        private readonly ValidateController _controller;
        private readonly ResourceDeserializer _resourceDeserializer;
        private readonly IMediator _mediator;

        public ValidateControllerTests()
        {
            var resource = new Patient()
            {
                Id = Guid.NewGuid().ToString(),
            };

            _mediator = Substitute.For<IMediator>();
            _mediator.Send<ValidateOperationResponse>(
                Arg.Any<ValidateOperationRequest>(),
                Arg.Any<CancellationToken>())
                .Returns(new ValidateOperationResponse(new OperationOutcomeIssue[0]));
            _mediator.Send<GetResourceResponse>(
                Arg.Any<GetResourceRequest>(),
                Arg.Any<CancellationToken>())
                .Returns(new GetResourceResponse(new RawResourceElement(
                    new ResourceWrapper(
                        resource.ToResourceElement(),
                        new RawResource(resource.ToJson(), FhirResourceFormat.Json, false),
                        null,
                        false,
                        null,
                        null,
                        null))));

            _resourceDeserializer = new ResourceDeserializer(
                (FhirResourceFormat.Json, new Func<string, string, DateTimeOffset, ResourceElement>((str, version, lastUpdated) =>
                {
                    return new Patient().ToResourceElement();
                })));

            _controller = new ValidateController(
                _mediator,
                _resourceDeserializer);
            _controller.ControllerContext = new ControllerContext(
                new ActionContext(
                    Substitute.For<HttpContext>(),
                    new RouteData(),
                    new ControllerActionDescriptor()));
        }

        [Theory]
        [InlineData("http://hl7.org/fhir/StructureDefinition/daf-patient", null, true)]
        [InlineData("invalid uri", null, false)]
        [InlineData(null, "http://hl7.org/fhir/StructureDefinition/daf-patient", true)]
        [InlineData(null, "invalid uri", false)]
        [InlineData("http://hl7.org/fhir/StructureDefinition/daf-patient", "http://hl7.org/fhir/StructureDefinition/daf-patient", false)]
        public async Task GivenResourceAndProfile_WhenValidating_ThenValidationShouldSucceed(
            string profile,
            string profileInParameters,
            bool valid)
        {
            var parameters = new Parameters();
            parameters.Parameter.Add(
                new Parameters.ParameterComponent()
                {
                    Name = "resource",
                    Resource = new Patient(),
                });

            if (!string.IsNullOrEmpty(profileInParameters))
            {
                parameters.Parameter.Add(
                    new Parameters.ParameterComponent()
                    {
                        Name = "profile",
                        Value = new FhirString(profileInParameters),
                    });
            }

            try
            {
                await _controller.Validate(parameters, profile);
                Assert.True(valid);
            }
            catch (BadRequestException)
            {
                Assert.False(valid);
            }

            await _mediator.Received(valid ? 1 : 0).Send<ValidateOperationResponse>(
                Arg.Any<ValidateOperationRequest>(),
                Arg.Any<CancellationToken>());
        }

        [Theory]
        [InlineData("http://hl7.org/fhir/StructureDefinition/daf-patient", true)]
        [InlineData("invalid uri", false)]
        [InlineData("", true)]
        [InlineData(null, true)]
        public async Task GivenResourceAndProfile_WhenValidatingById_ThenValidationShouldSucceed(
            string profile,
            bool valid)
        {
            try
            {
                await _controller.ValidateById(
                    KnownResourceTypes.Patient,
                    Guid.NewGuid().ToString(),
                    profile);
                Assert.True(valid);
            }
            catch (BadRequestException)
            {
                Assert.False(valid);
            }

            await _mediator.Received(valid ? 1 : 0).Send<ValidateOperationResponse>(
                Arg.Any<ValidateOperationRequest>(),
                Arg.Any<CancellationToken>());
            await _mediator.Received(valid ? 1 : 0).Send<GetResourceResponse>(
                Arg.Any<GetResourceRequest>(),
                Arg.Any<CancellationToken>());
        }

        [Theory]
        [InlineData("http://hl7.org/fhir/StructureDefinition/daf-patient", null, true, false, true)]
        [InlineData("http://hl7.org/fhir/StructureDefinition/daf-patient", null, true, true, true)]
        [InlineData("invalid uri", null, true, true, false)]
        [InlineData(null, "http://hl7.org/fhir/StructureDefinition/daf-patient", true, false, true)]
        [InlineData(null, "http://hl7.org/fhir/StructureDefinition/daf-patient", true, true, true)]
        [InlineData(null, "invalid uri", true, true, false)]
        [InlineData("http://hl7.org/fhir/StructureDefinition/daf-patient", "http://hl7.org/fhir/StructureDefinition/daf-patient", true, true, false)]
        [InlineData("http://hl7.org/fhir/StructureDefinition/daf-patient", null, false, false, true)]
        public async Task GivenResourceAndProfile_WhenValidatingByIdPost_ThenValidationShouldSucceed(
            string profile,
            string profileInParameters,
            bool passParameters,
            bool getResource,
            bool valid)
        {
            var parameters = new Parameters();
            parameters.Parameter.Add(
                new Parameters.ParameterComponent()
                {
                    Name = "resource",
                    Resource = getResource ? null : new Patient(),
                });

            if (!string.IsNullOrEmpty(profileInParameters))
            {
                parameters.Parameter.Add(
                    new Parameters.ParameterComponent()
                    {
                        Name = "profile",
                        Value = new FhirString(profileInParameters),
                    });
            }

            try
            {
                await _controller.ValidateByIdPost(
                    passParameters ? parameters : new Patient(),
                    KnownResourceTypes.Patient,
                    Guid.NewGuid().ToString(),
                    profile);
                Assert.True(valid);
            }
            catch (BadRequestException)
            {
                Assert.False(valid);
            }

            await _mediator.Received(valid ? 1 : 0).Send<ValidateOperationResponse>(
                Arg.Any<ValidateOperationRequest>(),
                Arg.Any<CancellationToken>());
            await _mediator.Received(valid && getResource ? 1 : 0).Send<GetResourceResponse>(
                Arg.Any<GetResourceRequest>(),
                Arg.Any<CancellationToken>());
        }

        private ICollection<OperationOutcomeIssue> CreateIssues(int count)
        {
            var issues = new List<OperationOutcomeIssue>();
            var severities = Enum.GetValues<OperationOutcome.IssueSeverity>();
            var types = Enum.GetValues<OperationOutcome.IssueType>();
            for (int i = 0; i < count; i++)
            {
                issues.Add(
                    new OperationOutcomeIssue(
                        severities[i % severities.Length].ToString(),
                        types[i % types.Length].ToString()));
            }

            return issues;
        }
    }
}
