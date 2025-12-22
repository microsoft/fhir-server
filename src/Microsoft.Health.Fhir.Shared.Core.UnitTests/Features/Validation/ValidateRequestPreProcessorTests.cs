// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentValidation;
using FluentValidation.Results;
using Medino;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Validation;
using Microsoft.Health.Fhir.Core.Messages.Upsert;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Validation
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public class ValidateRequestPreProcessorTests
    {
        [Fact]
        public async Task GivenARequest_WhenValidatingThatType_ThenAllValidationRulesShouldRun()
        {
            var mockValidator1 = Substitute.For<AbstractValidator<UpsertResourceRequest>>();
            var mockValidator2 = Substitute.For<AbstractValidator<UpsertResourceRequest>>();

            var validators = new[] { mockValidator1, mockValidator2 };
            var upsertValidationHandler = new ValidateRequestPreProcessor<UpsertResourceRequest, UpsertResourceResponse>(validators);
            var resource = Samples.GetDefaultObservation().UpdateId("observation1");
            var upsertResourceRequest = new UpsertResourceRequest(resource);
            var mockResponse = new UpsertResourceResponse(new SaveOutcome(CreateRawResourceElement(resource), SaveOutcomeType.Created));

            await upsertValidationHandler.HandleAsync(
                upsertResourceRequest,
                () => Task.FromResult(mockResponse),
                CancellationToken.None);

            await mockValidator1.Received().ValidateAsync(
                Arg.Is<ValidationContext<UpsertResourceRequest>>(ctx => ctx.InstanceToValidate == upsertResourceRequest),
                Arg.Any<CancellationToken>());
            await mockValidator2.Received().ValidateAsync(
                Arg.Is<ValidationContext<UpsertResourceRequest>>(ctx => ctx.InstanceToValidate == upsertResourceRequest),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenARequest_WhenValidatingThatTypeWithFailingRule_ThenAValidationExceptionShouldBeThrown()
        {
            var mockValidator1 = Substitute.For<AbstractValidator<UpsertResourceRequest>>();
            var mockValidator2 = Substitute.For<AbstractValidator<UpsertResourceRequest>>();

            var validators = new[] { mockValidator1, mockValidator2 };
            var upsertValidationHandler = new ValidateRequestPreProcessor<UpsertResourceRequest, UpsertResourceResponse>(validators);
            var resource = Samples.GetDefaultObservation().UpdateId("observation1");
            var upsertResourceRequest = new UpsertResourceRequest(resource);

            var validationError = Task.FromResult(new ValidationResult(new[] { new ValidationFailure("Id", "Id should not be null") }));

            mockValidator2
                .ValidateAsync(
                    Arg.Is<ValidationContext<UpsertResourceRequest>>(ctx => ctx.InstanceToValidate == upsertResourceRequest),
                    Arg.Any<CancellationToken>())
                .Returns(validationError);

            await Assert.ThrowsAsync<ResourceNotValidException>(
                async () => await upsertValidationHandler.HandleAsync(
                    upsertResourceRequest,
                    () => Task.FromResult(new UpsertResourceResponse(new SaveOutcome(CreateRawResourceElement(resource), SaveOutcomeType.Created))),
                    CancellationToken.None));
        }

        [Fact]
        public async Task GivenARequest_WhenReturningFhirValidationFailure_ThenOperationOutcomeIsUsedCorrectly()
        {
            var mockValidator = Substitute.For<AbstractValidator<UpsertResourceRequest>>();

            var validators = new[] { mockValidator };
            var upsertValidationHandler = new ValidateRequestPreProcessor<UpsertResourceRequest, UpsertResourceResponse>(validators);
            var resource = Samples.GetDefaultObservation().UpdateId("observation1");
            var upsertResourceRequest = new UpsertResourceRequest(resource);

            var operationOutcomeIssue = new OperationOutcomeIssue(
                OperationOutcomeConstants.IssueSeverity.Error,
                OperationOutcomeConstants.IssueType.Invalid,
                "Id was Invalid");

            var validationError = new ValidationResult(new[] { new FhirValidationFailure("Id", operationOutcomeIssue.Diagnostics, operationOutcomeIssue) });
            validationError.Errors[0].ErrorCode = "Custom";

            mockValidator
                .ValidateAsync(
                    Arg.Is<ValidationContext<UpsertResourceRequest>>(ctx => ctx.InstanceToValidate == upsertResourceRequest),
                    Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(validationError));

            var exception = await Assert.ThrowsAsync<ResourceNotValidException>(
                async () => await upsertValidationHandler.HandleAsync(
                    upsertResourceRequest,
                    () => Task.FromResult(new UpsertResourceResponse(new SaveOutcome(CreateRawResourceElement(resource), SaveOutcomeType.Created))),
                    CancellationToken.None));

            Assert.Contains(operationOutcomeIssue, exception.Issues);
        }

        private static RawResourceElement CreateRawResourceElement(ResourceElement resource)
        {
            var rawResource = new RawResource("data", FhirResourceFormat.Json, isMetaSet: true);
            var wrapper = new ResourceWrapper(
                resource,
                rawResource,
                new ResourceRequest(HttpMethod.Post, "http://fhir"),
                deleted: false,
                searchIndices: null,
                compartmentIndices: null,
                lastModifiedClaims: null);

            return new RawResourceElement(wrapper);
        }
    }
}
