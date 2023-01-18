// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using EnsureThat;
using FluentValidation;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Messages.Delete;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Resources.Create
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1710:Identifiers should have correct suffix", Justification = "Follows validator naming convention.")]
    public class ConditionalDeleteResourceValidator : AbstractValidator<ConditionalDeleteResourceRequest>
    {
        public ConditionalDeleteResourceValidator(IOptions<CoreFeatureConfiguration> configuration, IModelInfoProvider modelInfoProvider)
        {
            EnsureArg.IsNotNull(configuration?.Value, nameof(configuration));

            RuleFor(x => x.ResourceType)
                .Must(modelInfoProvider.IsKnownResource)
                .WithMessage(request => string.Format(CultureInfo.InvariantCulture, Core.Resources.ResourceNotSupported, request.ResourceType));

            RuleFor(x => x.ConditionalParameters)
                .Custom((conditionalParameters, context) =>
                {
                    if (conditionalParameters.Count == 0)
                    {
                        context.AddFailure(string.Format(CultureInfo.InvariantCulture, Core.Resources.ConditionalOperationNotSelectiveEnough, context.InstanceToValidate.ResourceType));
                    }
                });

            RuleFor(x => x.MaxDeleteCount)
                .InclusiveBetween(1, configuration.Value.ConditionalDeleteMaxItems)
                .WithMessage(string.Format(CultureInfo.InvariantCulture, Core.Resources.ConditionalDeleteCountOutOfRange, 1, configuration.Value.ConditionalDeleteMaxItems));
        }
    }
}
