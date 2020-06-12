// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Operations
{
    public class JobConflictException : FhirException
    {
        public JobConflictException()
            : base()
        {
        }

        public JobConflictException(string message)
            : base(message)
        {
            Issues.Add(new Models.OperationOutcomeIssue(
                OperationOutcomeConstants.IssueSeverity.Error,
                OperationOutcomeConstants.IssueType.Conflict,
                message));
        }
    }
}
