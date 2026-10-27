// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Exceptions
{
    public class CustomerManagedKeyException : FhirException
    {
        public CustomerManagedKeyException(string message)
            : this(message, null)
        {
        }

        /// <summary>
        /// Initializes a customer-managed key exception with its underlying cause.
        /// </summary>
        /// <param name="message">The error message.</param>
        /// <param name="innerException">The underlying exception.</param>
        public CustomerManagedKeyException(string message, Exception innerException)
            : base(message, innerException)
        {
            Issues.Add(new OperationOutcomeIssue(
                    OperationOutcomeConstants.IssueSeverity.Error,
                    OperationOutcomeConstants.IssueType.Invalid,
                    message));
        }
    }
}
