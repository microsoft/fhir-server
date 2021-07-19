// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Exceptions
{
    public class RequestNotValidException : FhirException
    {
        public RequestNotValidException(string message, string issueType = OperationOutcomeConstants.IssueType.Invalid)
            : base(message)
        {
            Debug.Assert(!string.IsNullOrEmpty(message));
            Debug.Assert(!string.IsNullOrEmpty(issueType));

            Issues.Add(new OperationOutcomeIssue(
                OperationOutcomeConstants.IssueSeverity.Error,
                issueType,
                message));
        }
    }
}
