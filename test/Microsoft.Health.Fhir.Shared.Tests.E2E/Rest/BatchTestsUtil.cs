// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
using Hl7.Fhir.Model;
using Xunit;
using static Hl7.Fhir.Model.OperationOutcome;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    internal static class BatchTestsUtil
    {
        public static void ValidateOperationOutcome(string actualStatusCode, OperationOutcome operationOutcome, string expectedStatusCode, string expectedDiagnostics, IssueType expectedIssueType)
        {
            Assert.Equal(expectedStatusCode, actualStatusCode);
            Assert.NotNull(operationOutcome);
            Assert.Single(operationOutcome.Issue);

            var issue = operationOutcome.Issue.First();

            Assert.Equal(IssueSeverity.Error, issue.Severity.Value);
            Assert.Equal(expectedIssueType, issue.Code);
            Assert.Equal(expectedDiagnostics, issue.Diagnostics);
        }
    }
}
