// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics;
using System.Net;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Operations.ConvertData.Models
{
    public class AzureContainerRegistryTokenException : FhirException
    {
        public AzureContainerRegistryTokenException(string message, HttpStatusCode statusCode)
            : base(message)
        {
            Debug.Assert(!string.IsNullOrEmpty(message), "Exception message should not be empty.");

            StatusCode = statusCode;
            Issues.Add(new OperationOutcomeIssue(
                OperationOutcomeConstants.IssueSeverity.Error,
                OperationOutcomeConstants.IssueType.Exception,
                message));
        }

        public AzureContainerRegistryTokenException(string message, HttpStatusCode statusCode, System.Exception innerException)
            : base(message, innerException)
        {
            Debug.Assert(!string.IsNullOrEmpty(message), "Exception message should not be empty.");
            Debug.Assert(innerException != null, "Inner exception should not be null.");

            StatusCode = statusCode;
            Issues.Add(new OperationOutcomeIssue(
                OperationOutcomeConstants.IssueSeverity.Error,
                OperationOutcomeConstants.IssueType.Exception,
                message));
        }

        public HttpStatusCode StatusCode { get; }
    }
}
