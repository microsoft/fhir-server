// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using FluentValidation.Results;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Validation
{
    public class FhirValidationFailure : ValidationFailure
    {
        public FhirValidationFailure(string propertyName, string errorMessage, OperationOutcomeIssue issueComponent)
            : base(propertyName, errorMessage)
        {
            EnsureArg.IsNotNullOrEmpty(propertyName, nameof(propertyName));
            EnsureArg.IsNotNullOrEmpty(errorMessage, nameof(errorMessage));
            EnsureArg.IsNotNull(issueComponent, nameof(issueComponent));

            IssueComponent = issueComponent;
        }

        public OperationOutcomeIssue IssueComponent { get; }
    }
}
