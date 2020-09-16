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
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1710:Identifiers should have correct suffix", Justification = "Follows validator naming convention.")]
    public class ResourceValidator : AbstractValidator<ResourceElement>
    {
        public ResourceValidator(INarrativeHtmlSanitizer narrativeHtmlSanitizer, IModelAttributeValidator modelAttributeValidator)
        {
            RuleFor(x => x.Id)
                .SetValidator(new IdValidator());
            RuleFor(x => x)
                .SetValidator(new AttributeValidator(modelAttributeValidator));
            RuleFor(x => x)
                .SetValidator(new NarrativeValidator(narrativeHtmlSanitizer));
        }
    }
}
