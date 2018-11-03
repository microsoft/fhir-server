// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Hl7.Fhir.Model;

namespace Microsoft.Health.Fhir.Core.Exceptions
{
    /// <summary>
    /// An exception indicating that the request rate has exceeded the maximum API request rate.
    /// </summary>
    public class RequestRateExceededException : FhirException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="RequestRateExceededException"/> class.
        /// </summary>
        /// <param name="retryAfter">The amount of time the client should wait before retrying again.</param>
        public RequestRateExceededException(TimeSpan? retryAfter)
        {
            RetryAfter = retryAfter;

            Issues.Add(new OperationOutcome.IssueComponent
            {
                Severity = OperationOutcome.IssueSeverity.Error,
                Code = OperationOutcome.IssueType.Throttled,
                Diagnostics = Resources.RequestRateExceeded,
            });
        }

        /// <summary>
        /// Gets the amount of time the client should wait before retrying again.
        /// </summary>
        public TimeSpan? RetryAfter { get; }
    }
}
