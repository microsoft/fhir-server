// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Exceptions
{
    public class ResourceNotFoundException : FhirException
    {
        public ResourceNotFoundException(string message, bool groupNotFound = false)
            : base(message)
        {
            Debug.Assert(!string.IsNullOrEmpty(message), "Exception message should not be empty");

            GroupNotFound = groupNotFound;
            Issues.Add(new OperationOutcomeIssue(
                    GroupNotFound ? OperationOutcomeConstants.IssueSeverity.Information : OperationOutcomeConstants.IssueSeverity.Error,
                    OperationOutcomeConstants.IssueType.NotFound,
                    message));
        }

        public bool GroupNotFound { get; init; } // Special case for group not found, will be turned into info log in the action filter.
    }
}
