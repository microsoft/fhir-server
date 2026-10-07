// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using FluentValidation;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Validation.FhirPrimitiveTypes;
using Microsoft.Health.Fhir.Core.Messages.Delete;

namespace Microsoft.Health.Fhir.Core.Features.Resources.Delete
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1710:Identifiers should have correct suffix", Justification = "Follows validator naming convention.")]
    public class DeleteResourceValidator : AbstractValidator<DeleteResourceRequest>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DeleteResourceValidator"/> class.
        /// </summary>
        /// <param name="config">The core feature configuration.</param>
        public DeleteResourceValidator(IOptions<CoreFeatureConfiguration> config)
        {
            int maxResourceIdLength = ResourceIdValidation.GetMaxLength(config.Value.UseLongResourceIds);

            RuleFor(x => x.ResourceKey.Id)
                .SetValidator(new IdValidator<DeleteResourceRequest>(config.Value.UseLongResourceIds))
                .WithMessage(string.Format(CultureInfo.InvariantCulture, Core.Resources.IdRequirements, maxResourceIdLength));
        }
    }
}
