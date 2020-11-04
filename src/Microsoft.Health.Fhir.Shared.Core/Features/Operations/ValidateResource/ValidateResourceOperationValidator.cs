// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using FluentValidation;
using Microsoft.Health.Fhir.Core.Features.Validation;
using Microsoft.Health.Fhir.Core.Features.Validation.Narratives;
using Microsoft.Health.Fhir.Core.Messages.Operation.Validate;

namespace Microsoft.Health.Fhir.Core.Messages.Operation
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1710:Identifiers should have correct suffix", Justification = "Follows validator naming convention.")]
    public class ValidateResourceOperationValidator : AbstractValidator<ValidateOperationRequest>
    {
        public ValidateResourceOperationValidator(INarrativeHtmlSanitizer htmlSanitizer, IModelAttributeValidator modelAttributeValidator)
        {
            RuleFor(x => x.Resource)
                .SetValidator(new ResourceValidator(htmlSanitizer, modelAttributeValidator));
        }
    }
}
