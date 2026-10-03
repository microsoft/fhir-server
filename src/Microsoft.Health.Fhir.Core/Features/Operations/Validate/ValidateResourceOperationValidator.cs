// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using FluentValidation;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Validation;
using Microsoft.Health.Fhir.Core.Features.Validation.Narratives;

namespace Microsoft.Health.Fhir.Core.Messages.Operation
{
    public class ValidateResourceOperationValidator : AbstractValidator<ValidateOperationRequest>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ValidateResourceOperationValidator"/> class.
        /// </summary>
        /// <param name="modelAttributeValidator">The model attribute validator.</param>
        /// <param name="narrativeHtmlSanitizer">The narrative HTML sanitizer.</param>
        /// <param name="config">The core feature configuration.</param>
        public ValidateResourceOperationValidator(IModelAttributeValidator modelAttributeValidator, INarrativeHtmlSanitizer narrativeHtmlSanitizer, IOptions<CoreFeatureConfiguration> config)
        {
            var attributeValidator = new ResourceContentValidator(modelAttributeValidator);
            RuleFor(x => x.Resource)
                .SetValidator(new ResourceElementValidator(attributeValidator, narrativeHtmlSanitizer, config.Value.MaxResourceIdLength));
        }
    }
}
