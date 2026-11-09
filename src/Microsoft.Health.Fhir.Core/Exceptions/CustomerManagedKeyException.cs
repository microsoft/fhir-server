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
        /// Initializes a new instance of the <see cref="CustomerManagedKeyException"/> class.
        /// </summary>
        /// <param name="message">The customer-managed key failure message.</param>
        /// <param name="innerException">The underlying storage exception.</param>
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
