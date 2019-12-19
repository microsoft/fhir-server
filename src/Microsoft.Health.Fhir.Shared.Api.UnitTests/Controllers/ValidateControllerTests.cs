// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.Model;
using MediatR;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Api.Controllers;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Operations;
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

            try
            {
                await disabledValidateController.Validate(payload);
                Assert.False(true);
            }
            catch (OperationNotImplementedException ex)
            {
                var enumerator = ex.Issues.GetEnumerator();
                enumerator.MoveNext();
                CheckOperationOutcomeIssue(
                    enumerator.Current.ToPoco(),
                    OperationOutcome.IssueSeverity.Error,
                    OperationOutcome.IssueType.NotSupported,
                    "$validate is not a supported endpoint.");
            }
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
            var featureConfiguration = new FeatureConfiguration
            {
                SupportsValidate = enableValidate,
            };
            IOptions<FeatureConfiguration> optionsFeatureConfiguration = Substitute.For<IOptions<FeatureConfiguration>>();
            optionsFeatureConfiguration.Value.Returns(featureConfiguration);

            return new ValidateController(_mediator, optionsFeatureConfiguration);
        }
    }
}
