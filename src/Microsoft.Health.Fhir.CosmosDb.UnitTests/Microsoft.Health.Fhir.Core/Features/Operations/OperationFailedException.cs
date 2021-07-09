// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Operations
{
    public class OperationFailedException : FhirException
    {
        public OperationFailedException(string message, HttpStatusCode statusCode)
            : base(message)
        {
            EnsureArg.IsNotNullOrWhiteSpace(message, nameof(message));

            ResponseStatusCode = statusCode;
            Issues.Add(new OperationOutcomeIssue(
                OperationOutcomeConstants.IssueSeverity.Error,
                OperationOutcomeConstants.IssueType.Processing,
                message));
        }

        public HttpStatusCode ResponseStatusCode { get; }
    }
}
