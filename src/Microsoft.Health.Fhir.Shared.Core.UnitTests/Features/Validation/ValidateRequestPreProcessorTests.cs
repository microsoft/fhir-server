// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using FluentValidation;
using FluentValidation.Results;
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
            var upsertValidationHandler = new ValidateRequestPreProcessor<UpsertResourceRequest>(validators);
            var upsertResourceRequest = new UpsertResourceRequest(Samples.GetDefaultObservation());

            await upsertValidationHandler.Process(upsertResourceRequest, CancellationToken.None);

            await mockValidator1.Received().ValidateAsync(
                Arg.Is<ValidationContext<UpsertResourceRequest>>(ctx => ctx.InstanceToValidate == upsertResourceRequest),
                Arg.Is<CancellationToken>(ct => ct == CancellationToken.None));
            await mockValidator2.Received().ValidateAsync(
                Arg.Is<ValidationContext<UpsertResourceRequest>>(ctx => ctx.InstanceToValidate == upsertResourceRequest),
                Arg.Is<CancellationToken>(ct => ct == CancellationToken.None));
        }

        [Fact]
        public async Task GivenARequest_WhenValidatingThatTypeWithFailingRule_ThenAValidationExceptionShouldBeThrown()
        {
            var mockValidator1 = Substitute.For<AbstractValidator<UpsertResourceRequest>>();
            var mockValidator2 = Substitute.For<AbstractValidator<UpsertResourceRequest>>();

            var validators = new[] { mockValidator1, mockValidator2 };
            var upsertValidationHandler = new ValidateRequestPreProcessor<UpsertResourceRequest>(validators);
            var upsertResourceRequest = new UpsertResourceRequest(Samples.GetDefaultObservation());

            var validationError = Task.FromResult(new ValidationResult(new[] { new ValidationFailure("Id", "Id should not be null") }));

            mockValidator2
                .ValidateAsync(
                    Arg.Is<ValidationContext<UpsertResourceRequest>>(ctx => ctx.InstanceToValidate == upsertResourceRequest),
                    Arg.Is<CancellationToken>(ct => ct == CancellationToken.None))
                .Returns(validationError);

            await Assert.ThrowsAsync<ResourceNotValidException>(
                async () => await upsertValidationHandler.Process(upsertResourceRequest, CancellationToken.None));
        }

        [Fact]
        public async Task GivenARequest_WhenReturningFhirValidationFailure_ThenOperationOutcomeIsUsedCorrectly()
        {
            var mockValidator = Substitute.For<AbstractValidator<UpsertResourceRequest>>();

            var validators = new[] { mockValidator };
            var upsertValidationHandler = new ValidateRequestPreProcessor<UpsertResourceRequest>(validators);
            var upsertResourceRequest = new UpsertResourceRequest(Samples.GetDefaultObservation());

            var operationOutcomeIssue = new OperationOutcomeIssue(
                OperationOutcomeConstants.IssueSeverity.Error,
                OperationOutcomeConstants.IssueType.Invalid,
                "Id was Invalid");

            var validationError = new ValidationResult(new[] { new FhirValidationFailure("Id", operationOutcomeIssue.Diagnostics, operationOutcomeIssue) });
            validationError.Errors[0].ErrorCode = "Custom";

            mockValidator
                .ValidateAsync(
                    Arg.Is<ValidationContext<UpsertResourceRequest>>(ctx => ctx.InstanceToValidate == upsertResourceRequest),
                    Arg.Is<CancellationToken>(ct => ct == CancellationToken.None))
                .Returns(Task.FromResult(validationError));

            var exception = await Assert.ThrowsAsync<ResourceNotValidException>(
                async () => await upsertValidationHandler.Process(upsertResourceRequest, CancellationToken.None));

            Assert.Contains(operationOutcomeIssue, exception.Issues);
        }
    }
}
