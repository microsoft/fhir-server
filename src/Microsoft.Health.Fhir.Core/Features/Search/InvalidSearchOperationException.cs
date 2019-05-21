// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Models;

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

            Issues.Add(new OperationOutcomeIssue(
                OperationOutcomeConstants.IssueSeverity.Error,
                OperationOutcomeConstants.IssueType.Forbidden,
                message));
        }
    }
}
