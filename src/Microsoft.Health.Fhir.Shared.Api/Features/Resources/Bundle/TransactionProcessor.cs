// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Net;
using Hl7.Fhir.Model;
using Microsoft.AspNetCore.Http;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Api.Features.Resources.Bundle
{
    public static class TransactionProcessor
    {
        public static void PreProcessBundleTransaction(Hl7.Fhir.Model.Bundle bundleResource)
        {
            BundleValidator.ValidateTransactionBundle(bundleResource);
        }

        public static void ThrowTransactionException(Hl7.Fhir.Model.Bundle.BundleType? bundleType, HttpContext httpContext, OperationOutcome operationOutcome)
        {
            if (bundleType != Hl7.Fhir.Model.Bundle.BundleType.TransactionResponse)
            {
                return;
            }

            var operationOutcomeIssues = GetOperationOutcomeIssues(operationOutcome.Issue);

            var errorMessage = string.Format(Api.Resources.TransactionFailed, httpContext.Request.Method, httpContext.Request.Path);

            throw new TransactionFailedException(errorMessage, (HttpStatusCode)httpContext.Response.StatusCode, operationOutcomeIssues);
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
