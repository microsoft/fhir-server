// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Operations.DataConvert.Models
{
    public class DataConvertTimeoutException : FhirException
    {
        public DataConvertTimeoutException()
        {
        }

        public DataConvertTimeoutException(string message)
            : base(message)
        {
            EnsureArg.IsNotNullOrWhiteSpace(message, nameof(message));
            Issues.Add(new OperationOutcomeIssue(
                OperationOutcomeConstants.IssueSeverity.Error,
                OperationOutcomeConstants.IssueType.Exception,
                message));
        }

        public DataConvertTimeoutException(string message, System.Exception innerException)
            : base(message, innerException)
        {
            EnsureArg.IsNotNullOrWhiteSpace(message, nameof(message));
            Issues.Add(new OperationOutcomeIssue(
                OperationOutcomeConstants.IssueSeverity.Error,
                OperationOutcomeConstants.IssueType.Exception,
                message));
        }
    }
}
