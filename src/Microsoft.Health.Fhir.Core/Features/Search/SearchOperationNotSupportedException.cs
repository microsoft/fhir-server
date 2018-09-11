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
    /// Thrown when search operation is not supported.
    /// </summary>
    public class SearchOperationNotSupportedException : FhirException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SearchOperationNotSupportedException"/> class.
        /// </summary>
        /// <param name="message">The error message.</param>
        public SearchOperationNotSupportedException(string message)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(message), $"{nameof(message)} should not be null or whitespace.");

            Issues.Add(new OperationOutcome.IssueComponent
            {
                Severity = OperationOutcome.IssueSeverity.Error,
                Code = OperationOutcome.IssueType.Forbidden,
                Diagnostics = message,
            });
        }
    }
}
