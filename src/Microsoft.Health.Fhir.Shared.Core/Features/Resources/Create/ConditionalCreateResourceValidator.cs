// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using EnsureThat;
using FluentValidation;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Validation;
using Microsoft.Health.Fhir.Core.Messages.Create;

namespace Microsoft.Health.Fhir.Core.Features.Resources.Create
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1710:Identifiers should have correct suffix", Justification = "Follows validator naming convention.")]
    public class ConditionalCreateResourceValidator : AbstractValidator<ConditionalCreateResourceRequest>
    {
        public ConditionalCreateResourceValidator(ILogger<ConditionalCreateResourceValidator> logger, IResourceIdentityValidator resourceIdentityValidator)
        {
            EnsureArg.IsNotNull(resourceIdentityValidator, nameof(resourceIdentityValidator));

            RuleFor(x => x.ConditionalParameters)
                .Custom((conditionalParameters, context) =>
                {
                    if (conditionalParameters.Count == 0)
                    {
                        logger?.LogInformation("PreconditionFailed: ConditionalOperationNotSelectiveEnough");
                        context.AddFailure(string.Format(CultureInfo.InvariantCulture, Core.Resources.ConditionalOperationNotSelectiveEnough, context.InstanceToValidate.ResourceType));
                    }
                });

            // A matching resource short-circuits the create, so the submitted identities are admitted here, before the search.
            RuleFor(x => x.Resource)
                .Custom((resource, context) =>
                {
                    foreach (var failure in resourceIdentityValidator.Validate(resource))
                    {
                        context.AddFailure(failure);
                    }
                });
        }
    }
}
