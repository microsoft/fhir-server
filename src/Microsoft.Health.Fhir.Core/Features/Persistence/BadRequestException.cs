// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Core.Features.Persistence
{
    public class BadRequestException : FhirException
    {
        public BadRequestException(string errorMessage)
        {
            Issues.Add(new OperationOutcomeIssue(
                IssueSeverity.Error,
                IssueType.Invalid,
                errorMessage));
        }
    }
}
