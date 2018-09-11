// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using EnsureThat;
using FluentValidation.Results;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Exceptions;

namespace Microsoft.Health.Fhir.Core.Features.Validation
{
    public class ResourceNotValidException : FhirException
    {
        public ResourceNotValidException(IEnumerable<OperationOutcome.IssueComponent> validationFailures)
        {
            EnsureArg.IsNotNull(validationFailures, nameof(validationFailures));

            foreach (var failure in validationFailures)
            {
                Issues.Add(failure);
            }
        }

        public ResourceNotValidException(IEnumerable<ValidationFailure> validationFailures)
        {
            EnsureArg.IsNotNull(validationFailures, nameof(validationFailures));

            foreach (var failure in validationFailures)
            {
                if (failure is FhirValidationFailure fhirValidationFailure)
                {
                    Issues.Add(fhirValidationFailure.IssueComponent);
                }
                else
                {
                    Issues.Add(new OperationOutcome.IssueComponent
                    {
                        Severity = OperationOutcome.IssueSeverity.Error,
                        Code = OperationOutcome.IssueType.Invalid,
                        Diagnostics = failure.ErrorMessage,
                    });
                }
            }
        }
    }
}
