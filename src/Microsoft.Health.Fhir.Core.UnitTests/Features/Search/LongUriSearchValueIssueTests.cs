// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class LongUriSearchValueIssueTests
    {
        [Fact]
        public void GivenSearchParameterCodes_WhenCreated_ThenTheWarningListsThemInOrderWithTheComparedLength()
        {
            OperationOutcomeIssue issue = LongUriSearchValueIssue.Create(new[] { "url", "_profile" }, 256);

            Assert.Equal(OperationOutcomeConstants.IssueSeverity.Warning, issue.Severity);
            Assert.Equal(OperationOutcomeConstants.IssueType.Informational, issue.Code);
            Assert.Equal(
                "The value of URI search parameter(s) '_profile, url' is 256 characters or longer. Only the first 256 characters of URI values are compared, so the results can include resources whose URI only shares those characters with the requested value.",
                issue.Diagnostics);
            Assert.True(LongUriSearchValueIssue.IsMatch(issue));
        }

        [Fact]
        public void GivenOtherIssues_WhenMatched_ThenTheyAreNotThisWarning()
        {
            string diagnostics = LongUriSearchValueIssue.Create(new[] { "_profile" }, 256).Diagnostics;

            Assert.False(LongUriSearchValueIssue.IsMatch(null));
            Assert.False(LongUriSearchValueIssue.IsMatch(new OperationOutcomeIssue(OperationOutcomeConstants.IssueSeverity.Warning, OperationOutcomeConstants.IssueType.NotSupported, "The search parameter 'x' is not supported.")));
            Assert.False(LongUriSearchValueIssue.IsMatch(new OperationOutcomeIssue(OperationOutcomeConstants.IssueSeverity.Error, OperationOutcomeConstants.IssueType.Informational, diagnostics)));
            Assert.False(LongUriSearchValueIssue.IsMatch(new OperationOutcomeIssue(OperationOutcomeConstants.IssueSeverity.Warning, OperationOutcomeConstants.IssueType.Invalid, diagnostics)));
            Assert.False(LongUriSearchValueIssue.IsMatch(new OperationOutcomeIssue(OperationOutcomeConstants.IssueSeverity.Warning, OperationOutcomeConstants.IssueType.Informational)));
        }
    }
}
