// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Net;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Api.Features.Bundle
{
    public sealed class FhirTransactionFailedException : BaseFhirTransactionException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FhirTransactionFailedException"/> class.
        /// Exception related to the processing of a FHIR transaction bundle.
        /// </summary>
        /// <param name="message">The exception message.</param>
        /// <param name="httpStatusCode">The status code to report to the user.</param>
        /// <param name="operationOutcomeIssues">A list of issues to include in the operation outcome.</param>
        public FhirTransactionFailedException(
            string message,
            HttpStatusCode httpStatusCode,
            IReadOnlyList<OperationOutcomeIssue> operationOutcomeIssues = null)
            : base(message, httpStatusCode, operationOutcomeIssues)
        {
        }

        public bool IsErrorCausedDueClientFailure()
        {
            // A client error is defined as a 4xx status code.
            // It can be caused by a malformed request, invalid data, precondition failures, etc.
            return ResponseStatusCode >= HttpStatusCode.BadRequest && ResponseStatusCode < HttpStatusCode.InternalServerError;
        }
    }
}
