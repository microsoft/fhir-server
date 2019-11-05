// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using System.Net;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Models;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Persistence
{
    public class TransactionFailedExceptionTests
    {
        [Fact]
        public void GivenAListOfOperationOutComeIssues_WhenInitialized_ThenCorrectOperationOutcomeIssuesShouldBeAdded()
        {
            string message = "message";

            HttpStatusCode statusCode = HttpStatusCode.Processing;
            var operationOutComeIssues = GetOperationOutcomeIssues(message);

            var exception = new TransactionFailedException(message, statusCode, operationOutComeIssues);

            Assert.NotNull(exception.Issues);
            Assert.Equal(3, exception.Issues.Count);

            var issue = exception.Issues.First();
            Assert.Equal(OperationOutcomeConstants.IssueSeverity.Error, issue.Severity);
            Assert.Equal(OperationOutcomeConstants.IssueType.Processing, issue.Code);
            Assert.Equal(message, issue.Diagnostics);

            issue = exception.Issues.Skip(1).First();
            Assert.Equal(OperationOutcomeConstants.IssueSeverity.Error, issue.Severity);
            Assert.Equal(OperationOutcomeConstants.IssueType.NotFound, issue.Code);
            Assert.Equal(message, issue.Diagnostics);

            issue = exception.Issues.Last();
            Assert.Equal(OperationOutcomeConstants.IssueSeverity.Error, issue.Severity);
            Assert.Equal(OperationOutcomeConstants.IssueType.Invalid, issue.Code);
            Assert.Equal(message, issue.Diagnostics);
        }

        private static List<OperationOutcomeIssue> GetOperationOutcomeIssues(string message)
        {
            var issues = new List<OperationOutcomeIssue>();
            issues.Add(new OperationOutcomeIssue(OperationOutcomeConstants.IssueSeverity.Error, OperationOutcomeConstants.IssueType.NotFound, message));
            issues.Add(new OperationOutcomeIssue(OperationOutcomeConstants.IssueSeverity.Error, OperationOutcomeConstants.IssueType.Invalid, message));
            return issues;
        }
    }
}
