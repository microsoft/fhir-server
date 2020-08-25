// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using FluentValidation;
using Microsoft.Health.Fhir.Core.Messages.Patch;

namespace Microsoft.Health.Fhir.Core.Features.Resources.Patch
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1710:Identifiers should have correct suffix", Justification = "Follows validator naming convention.")]
    public class ConditionalPatchResourceValidator : AbstractValidator<ConditionalPatchResourceRequest>
    {
        public ConditionalPatchResourceValidator()
        {
            RuleFor(x => x.ConditionalParameters)
                .NotEmpty().WithMessage(Core.Resources.ConditionalOperationNotSelectiveEnough);
        }
    }
}
