// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Operations
{
    public class OperationNotImplementedException : FhirException
    {
        public OperationNotImplementedException(string message)
            : base(message)
        {
            EnsureArg.IsNotNullOrWhiteSpace(message, nameof(message));

            Issues.Add(new OperationOutcomeIssue(
                OperationOutcomeConstants.IssueSeverity.Error,
                OperationOutcomeConstants.IssueType.NotSupported,
                message));
        }
    }
}
