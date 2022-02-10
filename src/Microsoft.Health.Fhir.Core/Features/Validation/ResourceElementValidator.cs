// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using FluentValidation;
using Microsoft.Health.Fhir.Core.Features.Validation.FhirPrimitiveTypes;
using Microsoft.Health.Fhir.Core.Features.Validation.Narratives;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Validation
{
    public class ResourceElementValidator : AbstractValidator<ResourceElement>
    {
        public ResourceElementValidator(AbstractValidator<ResourceElement> contentValidator, INarrativeHtmlSanitizer narrativeHtmlSanitizer)
        {
            RuleFor(x => x.Id)
              .SetValidator(new IdValidator<ResourceElement>()).WithMessage(Core.Resources.IdRequirements);
            RuleFor(x => x)
                  .SetValidator(contentValidator);
            RuleFor(x => x)
                .SetValidator(new NarrativeValidator(narrativeHtmlSanitizer));
        }
    }
}
