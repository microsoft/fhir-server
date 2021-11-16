// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Persistence
{
    public class BadRequestException : FhirException
    {
        public BadRequestException(string errorMessage)
            : base(errorMessage)
        {
            Issues.Add(new OperationOutcomeIssue(
                    OperationOutcomeConstants.IssueSeverity.Error,
                    OperationOutcomeConstants.IssueType.Invalid,
                    errorMessage));
        }

        /// <summary>
        /// Constructor creates BadRequestException from collection of error strings.
        /// </summary>
        /// <param name="errorMessages">Collection of error strings that are used to initialize OperationOutcomeIssue collection.
        /// Must be non-null and non-empty</param>
        public BadRequestException(ICollection<string> errorMessages)
            : base(((Func<string>)(() =>
            {
                Debug.Assert(errorMessages != null, "Parameter errorMessages must not be null.");
                Debug.Assert(errorMessages == null || errorMessages.Any(), "Parameter errorMessages must not be empty.");
                return (errorMessages == null || errorMessages.Count < 2) ? errorMessages.First() : "Multiple bad request errors."; // "Multiple bad request errors." message is used only internally, no need to add it to the Resources.resx file.
            }))())
        {
            foreach (string errorMessage in errorMessages)
            {
                Issues.Add(new OperationOutcomeIssue(
                        OperationOutcomeConstants.IssueSeverity.Error,
                        OperationOutcomeConstants.IssueType.Invalid,
                        errorMessage));
            }
        }
    }
}
