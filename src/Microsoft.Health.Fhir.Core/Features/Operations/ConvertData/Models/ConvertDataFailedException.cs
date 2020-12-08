// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Operations.ConvertData.Models
{
    public class ConvertDataFailedException : FhirException
    {
        public ConvertDataFailedException(string message)
            : base(message)
        {
            Debug.Assert(!string.IsNullOrEmpty(message), "Exception message should not be empty.");

            Issues.Add(new OperationOutcomeIssue(
                OperationOutcomeConstants.IssueSeverity.Error,
                OperationOutcomeConstants.IssueType.Exception,
                message));
        }

        public ConvertDataFailedException(string message, System.Exception innerException)
            : base(message, innerException)
        {
            Debug.Assert(!string.IsNullOrEmpty(message), "Exception message should not be empty.");
            Debug.Assert(innerException != null, "Inner exception should not be null.");

            Issues.Add(new OperationOutcomeIssue(
                OperationOutcomeConstants.IssueSeverity.Error,
                OperationOutcomeConstants.IssueType.Exception,
                message));
        }
    }
}
