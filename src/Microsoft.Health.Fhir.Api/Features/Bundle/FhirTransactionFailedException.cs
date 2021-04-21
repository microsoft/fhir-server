// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Api.Features.Bundle
{
    public class FhirTransactionFailedException : FhirException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FhirTransactionFailedException"/> class.
        /// Exception related to the processing of a FHIR transaction bundle.
        /// </summary>
        /// <param name="message">The exception message.</param>
        /// <param name="httpStatusCode">The status code to report to the user.</param>
        /// <param name="operationOutcomeIssues">A list of issues to include in the operation outcome.</param>
        public FhirTransactionFailedException(string message, HttpStatusCode httpStatusCode, IReadOnlyList<OperationOutcomeIssue> operationOutcomeIssues = null)
            : base(message)
        {
            Debug.Assert(!string.IsNullOrEmpty(message), "Exception message should not be empty");

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
