// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using FluentValidation;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Validation;
using Microsoft.Health.Fhir.Core.Features.Validation.Narratives;

namespace Microsoft.Health.Fhir.Core.Messages.Operation
{
    public class ValidateResourceOperationValidator : AbstractValidator<ValidateOperationRequest>
    {
        public ValidateResourceOperationValidator(IModelAttributeValidator modelAttributeValidator, INarrativeHtmlSanitizer narrativeHtmlSanitizer, ILogger logger)
        {
            var attributeValidator = new ResourceContentValidator(modelAttributeValidator, logger);
            RuleFor(x => x.Resource)
                .SetValidator(new ResourceElementValidator(attributeValidator, narrativeHtmlSanitizer, logger));
        }
    }
}
