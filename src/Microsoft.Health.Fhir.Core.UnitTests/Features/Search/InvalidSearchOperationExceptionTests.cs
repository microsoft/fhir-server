// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Search;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search
{
    public class InvalidSearchOperationExceptionTests
    {
        [Fact]
        public void GivenAMessage_WhenInitialized_ThenCorrectOperationOutcomeShouldBeAdded()
        {
            string message = "message";

            var exception = new InvalidSearchOperationException(message);

            Assert.NotNull(exception.Issues);
            Assert.Equal(1, exception.Issues.Count);

            OperationOutcome.IssueComponent issue = exception.Issues.First();

            Assert.Equal(OperationOutcome.IssueSeverity.Error, issue.Severity);
            Assert.Equal(OperationOutcome.IssueType.Forbidden, issue.Code);
            Assert.Equal(message, issue.Diagnostics);
        }
    }
}
