// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Net;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Api.Features.Bundle;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Api.Features.Resources.Bundle
{
    internal static class TransactionExceptionHandler
    {
        public static void ThrowTransactionException(string errorMessage, HttpStatusCode statusCode, OperationOutcome operationOutcome)
        {
            List<OperationOutcomeIssue> operationOutcomeIssues = GetOperationOutcomeIssues(operationOutcome.Issue);

            throw new FhirTransactionFailedException(errorMessage, statusCode, operationOutcomeIssues);
        }

        public static List<OperationOutcomeIssue> GetOperationOutcomeIssues(IReadOnlyList<OperationOutcome.IssueComponent> operationOutcomeIssues)
        {
            var issues = new List<OperationOutcomeIssue>();
            foreach (var issue in operationOutcomeIssues)
            {
                issues.Add(new OperationOutcomeIssue(
                     issue.Severity.ToString(),
                     issue.Code.ToString(),
                     issue.Diagnostics));
            }

            return issues;
        }
    }
}
