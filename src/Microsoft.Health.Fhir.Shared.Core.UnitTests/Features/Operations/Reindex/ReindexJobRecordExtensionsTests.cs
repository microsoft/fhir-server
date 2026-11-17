// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Reindex.Models;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Operations.Reindex
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.IndexAndReindex)]
    public class ReindexJobRecordExtensionsTests
    {
        [Fact]
        public void GivenAReindexJobWithOverlongReferenceIds_WhenConvertedToParameters_ThenAnIssuesParameterListsThem()
        {
            var record = new ReindexJobRecord();
            record.OverlongReferenceIds.Add(new OverlongReferenceIdCount("Observation", "performer", 64, 3));
            record.OverlongReferenceIds.Add(new OverlongReferenceIdCount("Patient", "general-practitioner", 64, 1));

            Parameters parameters = new ReindexJobWrapper(record, WeakETag.FromVersionId("1")).ToParametersResourceElement().ToPoco<Parameters>();

            var outcome = Assert.IsType<OperationOutcome>(Assert.Single(parameters.Parameter, p => p.Name == JobRecordProperties.Issues).Resource);
            Assert.Collection(
                outcome.Issue,
                issue =>
                {
                    Assert.Equal(OperationOutcome.IssueSeverity.Information, issue.Severity);
                    Assert.Equal(OperationOutcome.IssueType.Informational, issue.Code);
                    Assert.Equal("Reference parameter 'performer' ID exceeds 64 characters. Index truncation applied after 64 characters, which may yield over-inclusive results", issue.Diagnostics);
                    Assert.Equal("Resource type 'Observation': 3 resource(s) affected.", issue.Details.Text);
                },
                issue => Assert.Equal("Resource type 'Patient': 1 resource(s) affected.", issue.Details.Text));
        }

        [Fact]
        public void GivenAReindexJobWithoutOverlongReferenceIds_WhenConvertedToParameters_ThenThereIsNoIssuesParameter()
        {
            Parameters parameters = new ReindexJobWrapper(new ReindexJobRecord(), WeakETag.FromVersionId("1")).ToParametersResourceElement().ToPoco<Parameters>();

            Assert.DoesNotContain(parameters.Parameter, p => p.Name == JobRecordProperties.Issues);
        }
    }
}
