// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Net;
using Microsoft.Health.Fhir.Api.Features.Bundle;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Bundle
{
    [Trait("Traits.OwningTeam", OwningTeam.Fhir)]
    public class FhirTransactionFailedExceptionTests
    {
        [Fact]
        public void GivenAListOfOperationOutComeIssues_WhenInitialized_ThenCorrectOperationOutcomeIssuesShouldBeAdded()
        {
            string message = "message";
            HttpStatusCode statusCode = HttpStatusCode.Processing;
            var operationOutComeIssues = GetOperationOutcomeIssues(message);

            var exception = new FhirTransactionFailedException(message, statusCode, operationOutComeIssues);

            Assert.NotNull(exception.Issues);
            Assert.Equal(3, exception.Issues.Count);

            AssertOperationOutcomeIssue(message, OperationOutcomeConstants.IssueType.Processing, exception.Issues.First());
            AssertOperationOutcomeIssue(message, OperationOutcomeConstants.IssueType.NotFound, exception.Issues.Skip(1).First());
            AssertOperationOutcomeIssue(message, OperationOutcomeConstants.IssueType.Invalid, exception.Issues.Last());
        }

        [Fact]
        public void GivenAnEmptyListOfOperationOutComeIssues_WhenInitialized_ThenOneOperationOutcomeIssueShouldBeAdded()
        {
            string message = "message";
            HttpStatusCode statusCode = HttpStatusCode.Processing;

            var exception = new FhirTransactionFailedException(message, statusCode, Array.Empty<OperationOutcomeIssue>());

            Assert.NotNull(exception.Issues);
            Assert.Single(exception.Issues);

            AssertOperationOutcomeIssue(message, OperationOutcomeConstants.IssueType.Processing, exception.Issues.First());
        }

        private static void AssertOperationOutcomeIssue(string message, string expectedStatusCode, OperationOutcomeIssue issue)
        {
            Assert.Equal(OperationOutcomeConstants.IssueSeverity.Error, issue.Severity);
            Assert.Equal(expectedStatusCode, issue.Code);
            Assert.Equal(message, issue.Diagnostics);
        }

        private static OperationOutcomeIssue[] GetOperationOutcomeIssues(string message)
        {
            return new OperationOutcomeIssue[2]
            {
                new OperationOutcomeIssue(OperationOutcomeConstants.IssueSeverity.Error, OperationOutcomeConstants.IssueType.NotFound, message),
                new OperationOutcomeIssue(OperationOutcomeConstants.IssueSeverity.Error, OperationOutcomeConstants.IssueType.Invalid, message),
            };
        }
    }
}
