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
        /// <summary>
        /// Initializes a new instance of the <see cref="ResourceElementValidator"/> class.
        /// </summary>
        /// <param name="contentValidator">The resource content validator.</param>
        /// <param name="narrativeHtmlSanitizer">The narrative HTML sanitizer.</param>
        /// <param name="maxResourceIdLength">The maximum resource ID length.</param>
        public ResourceElementValidator(IValidator<ResourceElement> contentValidator, INarrativeHtmlSanitizer narrativeHtmlSanitizer, int maxResourceIdLength)
        {
            RuleFor(x => x.Id)
                .SetValidator(new IdValidator<ResourceElement>(maxResourceIdLength))
                .WithMessage(string.Format(CultureInfo.InvariantCulture, Core.Resources.IdRequirements, maxResourceIdLength));
            RuleFor(x => x)
                .SetValidator(contentValidator);
            RuleFor(x => x)
                .SetValidator(new NarrativeValidator(narrativeHtmlSanitizer));
        }
    }
}
