// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using FluentValidation.Results;
using Hl7.Fhir.Model;

namespace Microsoft.Health.Fhir.Core.Features.Validation
{
    public class FhirValidationFailure : ValidationFailure
    {
        public FhirValidationFailure(string propertyName, string errorMessage, OperationOutcome.IssueComponent issueComponent)
            : base(propertyName, errorMessage)
        {
            IssueComponent = issueComponent;
        }

        public FhirValidationFailure(string propertyName, string errorMessage)
            : base(propertyName, errorMessage)
        {
        }

        public FhirValidationFailure(string propertyName, string errorMessage, object attemptedValue)
            : base(propertyName, errorMessage, attemptedValue)
        {
        }

        public OperationOutcome.IssueComponent IssueComponent { get; }
    }
}
