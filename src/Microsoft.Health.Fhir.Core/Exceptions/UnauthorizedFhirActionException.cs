// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Exceptions
{
    public class UnauthorizedFhirActionException : FhirException
    {
        public UnauthorizedFhirActionException()
            : this(Resources.Forbidden)
        {
        }

        public UnauthorizedFhirActionException(string diagnostics)
        {
            Issues.Add(new OperationOutcomeIssue(
                OperationOutcomeConstants.IssueSeverity.Error,
                OperationOutcomeConstants.IssueType.Forbidden,
                string.IsNullOrWhiteSpace(diagnostics) ? Resources.Forbidden : diagnostics));
        }
    }
}
