// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Exceptions;

namespace Microsoft.Health.Fhir.Core.Features.Search
{
    /// <summary>
    /// Exception thrown when an invalid search operation is specified.
    /// </summary>
    public class InvalidSearchOperationException : FhirException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="InvalidSearchOperationException"/> class.
        /// </summary>
        /// <param name="message">The message to display.</param>
        public InvalidSearchOperationException(string message)
            : base(message)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(message), $"{nameof(message)} should not be null.");

            Issues.Add(new OperationOutcome.IssueComponent
            {
                Severity = OperationOutcome.IssueSeverity.Error,
                Code = OperationOutcome.IssueType.Forbidden,
                Diagnostics = message,
            });
        }
    }
}
