// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
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
                "URI parameter '_profile, url' exceeds 256 characters. Evaluation is limited to the first 256 characters, which may return broader search results.",
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

        [Fact]
        public void GivenWarningsForDifferentParameters_WhenAddedOrMerged_ThenOneWarningNamesAllOfThemInPlace()
        {
            var other = new OperationOutcomeIssue(OperationOutcomeConstants.IssueSeverity.Warning, OperationOutcomeConstants.IssueType.NotSupported, "other");
            var issues = new List<OperationOutcomeIssue>();

            Assert.True(LongUriSearchValueIssue.AddOrMerge(issues, new[] { "url" }, 256));
            issues.Add(other);
            Assert.True(LongUriSearchValueIssue.AddOrMerge(issues, new[] { "_profile" }, 256));
            Assert.False(LongUriSearchValueIssue.AddOrMerge(issues, new[] { "url", "_profile" }, 256));
            Assert.False(LongUriSearchValueIssue.AddOrMerge(issues, new[] { "url" }, 256));

            Assert.Equal(2, issues.Count);
            Assert.Equal(LongUriSearchValueIssue.Create(new[] { "_profile", "url" }, 256).Diagnostics, issues[0].Diagnostics);
            Assert.True(LongUriSearchValueIssue.IsMatch(issues[0]));
            Assert.Same(other, issues[1]);
        }
    }
}
