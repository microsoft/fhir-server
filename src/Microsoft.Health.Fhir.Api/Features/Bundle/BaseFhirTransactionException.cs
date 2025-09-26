// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Net;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Api.Features.Bundle
{
    public abstract class BaseFhirTransactionException : FhirException
    {
        protected BaseFhirTransactionException(
            string message,
            HttpStatusCode httpStatusCode,
            IReadOnlyList<OperationOutcomeIssue> operationOutcomeIssues = null)
            : base(message)
        {
            EnsureArg.IsNotNullOrWhiteSpace(message, nameof(message));

            ResponseStatusCode = httpStatusCode;

            Issues.Add(new OperationOutcomeIssue(
                OperationOutcomeConstants.IssueSeverity.Error,
                OperationOutcomeConstants.IssueType.Processing,
                message));

            if (operationOutcomeIssues != null)
            {
                foreach (var issue in operationOutcomeIssues)
                {
                    Issues.Add(issue);
                }
            }
        }

        public HttpStatusCode ResponseStatusCode { get; }
    }
}
