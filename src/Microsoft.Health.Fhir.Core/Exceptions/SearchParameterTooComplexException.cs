// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Exceptions
{
    /// <summary>
    /// The exception that is thrown when the search parameter is too complex.
    /// In the case of input search parameter being too complex, there is a possibility of a stack overflow. Stack overflow exceptions cannot be
    /// caught in .NET and will abort the process. For that reason we throw this exceptioon when predefined stack depth limit is reached.
    /// </summary>
    public class SearchParameterTooComplexException : Core.Exceptions.FhirException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SearchParameterTooComplexException"/> class.
        /// </summary>
        public SearchParameterTooComplexException()
        {
            Issues.Add(new OperationOutcomeIssue(
                OperationOutcomeConstants.IssueSeverity.Error,
                OperationOutcomeConstants.IssueType.NotSupported,
                "Search parameter too complex."));
        }
    }
}
