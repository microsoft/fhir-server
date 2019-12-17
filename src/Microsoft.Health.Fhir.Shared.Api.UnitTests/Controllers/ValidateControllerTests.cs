// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.Model;
using MediatR;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Api.Controllers;
using Microsoft.Health.Fhir.Api.Features.ActionResults;
using Microsoft.Health.Fhir.Core.Configs;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Controllers
{
    public class ValidateControllerTests
    {
        private ValidateController _validateController;
        private IMediator _mediator = Substitute.For<IMediator>();

        public ValidateControllerTests()
        {
            _validateController = GetController(true);
        }

        [Fact]
        public async void GivenAValidateRequest_WhenTheServerDoesNotSupportValidate_ThenANotSupportedErrorIsReturned()
        {
            var disabledValidateController = GetController(false);
            var payload = new Observation();

            var result = (FhirResult)await disabledValidateController.Validate(payload);
            var operationOutcome = (OperationOutcome)result.Result.Instance;

            CheckOperationOutcomeIssue(
                operationOutcome.Issue.GetEnumerator().Current,
                OperationOutcome.IssueSeverity.Error,
                OperationOutcome.IssueType.NotSupported,
                "Not supported");
        }

        private void CheckOperationOutcomeIssue(
            OperationOutcome.IssueComponent issue,
            OperationOutcome.IssueSeverity expectedSeverity,
            OperationOutcome.IssueType expectedCode,
            string expectedMessage)
        {
            // Check expected outcome
            Assert.Equal(expectedSeverity, issue.Severity);
            Assert.Equal(expectedCode, issue.Code);
            Assert.Equal(expectedMessage, issue.Diagnostics);
        }

        private ValidateController GetController(bool enableValidate)
        {
            var coreFeatureConfiguration = new CoreFeatureConfiguration
            {
                SupportsValidate = enableValidate,
            };
            IOptions<CoreFeatureConfiguration> optionsCoreFeatureConfiguration = Substitute.For<IOptions<CoreFeatureConfiguration>>();
            optionsCoreFeatureConfiguration.Value.Returns(coreFeatureConfiguration);

            return new ValidateController(_mediator, optionsCoreFeatureConfiguration);
        }
    }
}
