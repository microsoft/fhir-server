// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Net;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Api.Features.Bundle
{
    public sealed class FhirTransactionCancelledException : BaseFhirTransactionException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FhirTransactionCancelledException"/> class.
        /// Exception related to the processing of a FHIR transaction bundle.
        /// </summary>
        /// <param name="message">The exception message.</param>
        /// <param name="operationOutcomeIssues">A list of issues to include in the operation outcome.</param>
        public FhirTransactionCancelledException(
            string message,
            IReadOnlyList<OperationOutcomeIssue> operationOutcomeIssues = null)
            : base(
                message,
                HttpStatusCode.RequestTimeout, // Use 408 Request Timeout to indicate that the client has cancelled.
                operationOutcomeIssues)
        {
        }
    }
}
