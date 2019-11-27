// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Api.Features.Resources.Bundle
{
    public static class TransactionExceptionHandler
    {
        public static void ThrowTransactionException(string method, string path, string statusCode, OperationOutcome operationOutcome)
        {
            var operationOutcomeIssues = GetOperationOutcomeIssues(operationOutcome.Issue);

            var errorMessage = string.Format(Api.Resources.TransactionFailed, method, path);

            if (!Enum.TryParse(statusCode, out HttpStatusCode httpStatusCode))
            {
                httpStatusCode = HttpStatusCode.BadRequest;
            }

            throw new TransactionFailedException(errorMessage, httpStatusCode, operationOutcomeIssues);
        }

        public static List<OperationOutcomeIssue> GetOperationOutcomeIssues(List<OperationOutcome.IssueComponent> operationoutcomeIssueList)
        {
            var issues = new List<OperationOutcomeIssue>();

            operationoutcomeIssueList.ForEach(x =>
                issues.Add(new OperationOutcomeIssue(
                    x.Severity.ToString(),
                    x.Code.ToString(),
                    x.Diagnostics)));

            return issues;
        }
    }
}
