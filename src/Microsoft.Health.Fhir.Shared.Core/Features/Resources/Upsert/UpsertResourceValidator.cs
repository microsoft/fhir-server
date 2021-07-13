// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using FluentValidation;
using Microsoft.Extensions.Options;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Validation;
using Microsoft.Health.Fhir.Core.Features.Validation.Narratives;
using Microsoft.Health.Fhir.Core.Messages.Upsert;

namespace Microsoft.Health.Fhir.Core.Features.Resources.Upsert
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1710:Identifiers should have correct suffix", Justification = "Follows validator naming convention.")]
    public class UpsertResourceValidator : AbstractValidator<UpsertResourceRequest>
    {
        public UpsertResourceValidator(
            IModelAttributeValidator modelAttributeValidator,
            INarrativeHtmlSanitizer narrativeHtmlSanitizer,
            IProfileValidator profileValidator,
            RequestContextAccessor<IFhirRequestContext> fhirRequestContextAccessor,
            IOptions<CoreFeatureConfiguration> config)
        {
            RuleFor(x => x.Resource.Id)
                .NotEmpty().WithMessage(Core.Resources.UpdateRequestsRequireId);

            var contentValidator = new ResourceProfileValidator(
              modelAttributeValidator,
              profileValidator,
              fhirRequestContextAccessor,
              config.Value.ProfileValidationOnUpdate);

            RuleFor(x => x.Resource)
                .SetValidator(new ResourceElementValidator(contentValidator, narrativeHtmlSanitizer));
        }
    }
}
