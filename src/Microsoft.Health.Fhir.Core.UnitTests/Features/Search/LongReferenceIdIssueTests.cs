// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class LongReferenceIdIssueTests
    {
        private const string PerformerWarning = "Reference parameter 'performer' ID exceeds 64 characters. Index truncation applied after 64 characters, which may yield over-inclusive results";

        [Fact]
        public void GivenASearchParameterCode_WhenCreated_ThenTheWarningHasTheExactMessage()
        {
            OperationOutcomeIssue issue = LongReferenceIdIssue.Create("performer", 64);

            Assert.Equal(OperationOutcomeConstants.IssueSeverity.Warning, issue.Severity);
            Assert.Equal(OperationOutcomeConstants.IssueType.Informational, issue.Code);
            Assert.Equal(PerformerWarning, issue.Diagnostics);
            Assert.True(LongReferenceIdIssue.IsMatch(issue));
            Assert.True(PartiallyComparedSearchValueIssue.IsMatch(issue));
        }

        [Fact]
        public void GivenTheLongResourceIdLimit_WhenCreated_ThenTheMessageUsesIt()
        {
            Assert.Equal(
                "Reference parameter 'subject' ID exceeds 128 characters. Index truncation applied after 128 characters, which may yield over-inclusive results",
                LongReferenceIdIssue.Create("subject", 128).Diagnostics);
        }

        [Fact]
        public void GivenACount_WhenCreatedForAJob_ThenTheIssueIsInformationalWithTheAffectedResources()
        {
            OperationOutcomeIssue issue = LongReferenceIdIssue.CreateForJob(new OverlongReferenceIdCount("Observation", "performer", 64, 3));

            Assert.Equal(OperationOutcomeConstants.IssueSeverity.Information, issue.Severity);
            Assert.Equal(OperationOutcomeConstants.IssueType.Informational, issue.Code);
            Assert.Equal(PerformerWarning, issue.Diagnostics);
            Assert.Equal("Resource type 'Observation': 3 resource(s) affected.", issue.DetailsText);
            Assert.True(LongReferenceIdIssue.IsMatch(issue));
        }

        [Fact]
        public void GivenTheSameWarningTwice_WhenAdded_ThenItIsAddedOncePerParameter()
        {
            var issues = new List<OperationOutcomeIssue>();

            Assert.True(LongReferenceIdIssue.TryAdd(issues, "performer", 64));
            Assert.False(LongReferenceIdIssue.TryAdd(issues, "performer", 64));
            Assert.True(LongReferenceIdIssue.TryAdd(issues, "subject", 64));

            Assert.Collection(
                issues,
                i => Assert.Equal(PerformerWarning, i.Diagnostics),
                i => Assert.Equal(LongReferenceIdIssue.Create("subject", 64).Diagnostics, i.Diagnostics));
        }

        [Fact]
        public void GivenOtherIssues_WhenMatched_ThenTheyAreNotThisWarning()
        {
            OperationOutcomeIssue uriWarning = LongUriSearchValueIssue.Create(new[] { "_profile" }, 256);

            Assert.False(LongReferenceIdIssue.IsMatch(null));
            Assert.False(LongReferenceIdIssue.IsMatch(uriWarning));
            Assert.False(LongReferenceIdIssue.IsMatch(new OperationOutcomeIssue(OperationOutcomeConstants.IssueSeverity.Warning, OperationOutcomeConstants.IssueType.NotSupported, "The search parameter 'x' is not supported.")));
            Assert.False(LongReferenceIdIssue.IsMatch(new OperationOutcomeIssue(OperationOutcomeConstants.IssueSeverity.Error, OperationOutcomeConstants.IssueType.Informational, PerformerWarning)));
            Assert.False(LongReferenceIdIssue.IsMatch(new OperationOutcomeIssue(OperationOutcomeConstants.IssueSeverity.Warning, OperationOutcomeConstants.IssueType.Invalid, PerformerWarning)));
            Assert.False(LongReferenceIdIssue.IsMatch(new OperationOutcomeIssue(OperationOutcomeConstants.IssueSeverity.Warning, OperationOutcomeConstants.IssueType.Informational)));
        }

        [Fact]
        public void GivenSearchWarnings_WhenMatchedAsPartiallyCompared_ThenOnlyTheUriAndReferenceIdWarningsMatch()
        {
            Assert.True(PartiallyComparedSearchValueIssue.IsMatch(LongUriSearchValueIssue.Create(new[] { "_profile" }, 256)));
            Assert.True(PartiallyComparedSearchValueIssue.IsMatch(LongReferenceIdIssue.Create("subject", 128)));
            Assert.False(PartiallyComparedSearchValueIssue.IsMatch(null));
            Assert.False(PartiallyComparedSearchValueIssue.IsMatch(new OperationOutcomeIssue(OperationOutcomeConstants.IssueSeverity.Warning, OperationOutcomeConstants.IssueType.NotSupported, "The search parameter 'x' is not supported.")));
        }
    }
}
