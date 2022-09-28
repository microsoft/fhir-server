// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search
{
    [Trait("Traits.OwningTeam", OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class InvalidSearchOperationExceptionTests
    {
        [Fact]
        public void GivenAMessage_WhenInitialized_ThenCorrectOperationOutcomeShouldBeAdded()
        {
            string message = "message";

            var exception = new InvalidSearchOperationException(message);

            Assert.NotNull(exception.Issues);
            Assert.Equal(1, exception.Issues.Count);

            var issue = exception.Issues.First();

            Assert.Equal(OperationOutcomeConstants.IssueSeverity.Error, issue.Severity);
            Assert.Equal(OperationOutcomeConstants.IssueType.Invalid, issue.Code);
            Assert.Equal(message, issue.Diagnostics);
        }
    }
}
