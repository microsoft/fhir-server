// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Globalization;
using FluentValidation;
using Microsoft.Health.Fhir.Core.Features.Validation.FhirPrimitiveTypes;
using Microsoft.Health.Fhir.Core.Features.Validation.Narratives;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Validation
{
    public class ResourceElementValidator : AbstractValidator<ResourceElement>
    {
        public ResourceElementValidator(IValidator<ResourceElement> contentValidator, INarrativeHtmlSanitizer narrativeHtmlSanitizer, ResourceIdPolicy resourceIdPolicy)
        {
            RuleFor(x => x.Id)
                .SetValidator(new IdValidator<ResourceElement>(resourceIdPolicy))
                .WithMessage(string.Format(CultureInfo.InvariantCulture, Core.Resources.IdRequirements, resourceIdPolicy.MaxLength));
            RuleFor(x => x)
                .SetValidator(contentValidator);
            RuleFor(x => x)
                .SetValidator(new NarrativeValidator(narrativeHtmlSanitizer));
        }
    }
}
