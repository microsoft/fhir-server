// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Persistence
{
    public class TransactionFailedException : FhirException
    {
        public TransactionFailedException(string message, HttpStatusCode httpStatusCode, List<OperationOutcomeIssue> operationOutcomeIssues = null)
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
                operationOutcomeIssues.ForEach(x => Issues.Add(x));
            }
        }

        public HttpStatusCode ResponseStatusCode { get; }
    }
}
