// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Globalization;
using EnsureThat;
using FluentValidation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Messages.Delete;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Resources.Create
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1710:Identifiers should have correct suffix", Justification = "Follows validator naming convention.")]
    public class ConditionalDeleteResourceValidator : AbstractValidator<ConditionalDeleteResourceRequest>
    {
        public ConditionalDeleteResourceValidator(IOptions<CoreFeatureConfiguration> configuration, IModelInfoProvider modelInfoProvider, ILogger<ConditionalDeleteResourceValidator> logger)
        {
            EnsureArg.IsNotNull(configuration?.Value, nameof(configuration));

            RuleFor(x => x.ResourceType)
                .Must(x => modelInfoProvider.IsKnownResource(x))
                .WithMessage(request => string.Format(CultureInfo.InvariantCulture, Core.Resources.ResourceNotSupported, request.ResourceType));

            RuleFor(x => x.ConditionalParameters)
                .Custom((conditionalParameters, context) =>
                {
                    if (conditionalParameters.Count == 0)
                    {
                        logger?.LogInformation("PreconditionFailed: ConditionalOperationNotSelectiveEnough");
                        context.AddFailure(string.Format(CultureInfo.InvariantCulture, Core.Resources.ConditionalOperationNotSelectiveEnough, context.InstanceToValidate.ResourceType));
                    }

                    foreach (var parameter in conditionalParameters)
                    {
                        var parameterName = parameter.Item1.Split(':')[0];
                        if (string.Equals(parameterName, "_include", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(parameterName, "_revinclude", StringComparison.OrdinalIgnoreCase))
                        {
                            context.AddFailure(string.Format(CultureInfo.InvariantCulture, Core.Resources.ConditionalDeleteIncludesNotSupported, parameter.Item1));
                        }
                    }
                });

            RuleFor(x => x.MaxDeleteCount)
                .InclusiveBetween(1, configuration.Value.ConditionalDeleteMaxItems)
                .WithMessage(string.Format(CultureInfo.InvariantCulture, Core.Resources.ConditionalDeleteCountOutOfRange, 1, configuration.Value.ConditionalDeleteMaxItems));
        }
    }
}
