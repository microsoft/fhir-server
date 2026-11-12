// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using EnsureThat;
using FluentValidation;
using Microsoft.Health.Fhir.Core.Features.Validation.Narratives;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Validation
{
    public class ResourceElementValidator : AbstractValidator<ResourceElement>
    {
        public ResourceElementValidator(IValidator<ResourceElement> contentValidator, INarrativeHtmlSanitizer narrativeHtmlSanitizer, IResourceIdentityValidator resourceIdentityValidator)
        {
            EnsureArg.IsNotNull(resourceIdentityValidator, nameof(resourceIdentityValidator));

            RuleFor(x => x)
                .Custom((resource, context) =>
                {
                    foreach (var failure in resourceIdentityValidator.Validate(resource))
                    {
                        context.AddFailure(failure);
                    }
                });
            RuleFor(x => x)
                .SetValidator(contentValidator);
            RuleFor(x => x)
                .SetValidator(new NarrativeValidator(narrativeHtmlSanitizer));
        }
    }
}
