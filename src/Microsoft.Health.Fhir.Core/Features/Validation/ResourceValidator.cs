// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using FluentValidation;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Validation.FhirPrimitiveTypes;
using Microsoft.Health.Fhir.Core.Features.Validation.Narratives;

namespace Microsoft.Health.Fhir.Core.Features.Validation
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1710:Identifiers should have correct suffix", Justification = "Follows validator naming convention.")]
    public class ResourceValidator : AbstractValidator<Resource>
    {
        public ResourceValidator(INarrativeHtmlSanitizer narrativeHtmlSanitizer)
        {
            RuleFor(x => x.Id)
                .SetValidator(new IdValidator());
            RuleFor(x => x)
                .SetValidator(new AttributeValidator());
            RuleFor(x => x)
                .SetValidator(new NarrativeValidator(narrativeHtmlSanitizer));
        }
    }
}
