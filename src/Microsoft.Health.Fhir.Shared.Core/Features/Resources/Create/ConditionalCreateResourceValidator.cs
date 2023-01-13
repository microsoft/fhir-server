// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using FluentValidation;
using Microsoft.Health.Fhir.Core.Messages.Create;

namespace Microsoft.Health.Fhir.Core.Features.Resources.Create
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1710:Identifiers should have correct suffix", Justification = "Follows validator naming convention.")]
    public class ConditionalCreateResourceValidator : AbstractValidator<ConditionalCreateResourceRequest>
    {
        public ConditionalCreateResourceValidator()
        {
            RuleFor(x => x.ConditionalParameters)
                .Custom((conditionalParameters, context) =>
                {
                    if (conditionalParameters.Count == 0)
                    {
                        context.AddFailure(string.Format(CultureInfo.InvariantCulture, Core.Resources.ConditionalOperationNotSelectiveEnough, context.InstanceToValidate.ResourceType));
                    }
                });
        }
    }
}
