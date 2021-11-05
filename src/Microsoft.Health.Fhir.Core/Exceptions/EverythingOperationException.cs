// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics;
using System.Net;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Exceptions
{
    public class EverythingOperationException : FhirException
    {
        public EverythingOperationException(string message, HttpStatusCode httpStatusCode, string contentLocationHeaderValue = null)
            : base(message)
        {
            Debug.Assert(!string.IsNullOrEmpty(message), "Exception message should not be empty.");

            ResponseStatusCode = httpStatusCode;
            ContentLocationHeaderValue = contentLocationHeaderValue;

            Issues.Add(new OperationOutcomeIssue(
                OperationOutcomeConstants.IssueSeverity.Error,
                OperationOutcomeConstants.IssueType.Invalid,
                message));
        }

        public HttpStatusCode ResponseStatusCode { get; }

        public string ContentLocationHeaderValue { get; }
    }
}
