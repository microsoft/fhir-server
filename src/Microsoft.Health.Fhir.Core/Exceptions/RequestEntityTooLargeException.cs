// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Exceptions
{
    public class RequestEntityTooLargeException : FhirException
    {
        public RequestEntityTooLargeException()
        {
            Issues.Add(new OperationOutcomeIssue(
                    OperationOutcomeConstants.IssueSeverity.Error,
                    OperationOutcomeConstants.IssueType.Invalid,
                    Resources.ResourceTooLarge));
        }

        public RequestEntityTooLargeException(string message)
            : base(message)
        {
            Issues.Add(new Models.OperationOutcomeIssue(
                OperationOutcomeConstants.IssueSeverity.Error,
                OperationOutcomeConstants.IssueType.Invalid,
                message));
        }

        public RequestEntityTooLargeException(string message, Exception exception)
            : base(message, exception)
        {
            Issues.Add(new Models.OperationOutcomeIssue(
                OperationOutcomeConstants.IssueSeverity.Error,
                OperationOutcomeConstants.IssueType.Invalid,
                message));
        }
    }
}
