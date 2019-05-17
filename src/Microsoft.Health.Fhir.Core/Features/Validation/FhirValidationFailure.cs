// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using FluentValidation.Results;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Validation
{
    public class FhirValidationFailure : ValidationFailure
    {
        public FhirValidationFailure(string propertyName, string errorMessage, IList<OperationOutcomeIssue> issueComponent)
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

        public IList<OperationOutcomeIssue> IssueComponent { get; }
    }
}
