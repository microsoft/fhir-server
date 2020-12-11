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
    /// The exception that is thrown when the search parameter is not supported.
    /// </summary>
    public class ConfigureCustomSearchException : FhirException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ConfigureCustomSearchException"/> class.
        /// </summary>
        /// <param name="error">The error message to include in the operation outcome issues list.</param>
        public ConfigureCustomSearchException(string error)
        {
            Debug.Assert(!string.IsNullOrEmpty(error), "Exception message should not be empty");

            AddIssue(error);
        }

        private void AddIssue(string diagnostics)
        {
            Issues.Add(new OperationOutcomeIssue(
                OperationOutcomeConstants.IssueSeverity.Error,
                OperationOutcomeConstants.IssueType.Exception,
                diagnostics));
        }
    }
}
