// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
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

        public BadRequestException(IEnumerable<string> errorMessages)
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
