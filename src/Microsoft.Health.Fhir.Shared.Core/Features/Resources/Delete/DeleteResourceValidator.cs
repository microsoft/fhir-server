// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using FluentValidation;
using Microsoft.Health.Fhir.Core.Features.Validation.FhirPrimitiveTypes;
using Microsoft.Health.Fhir.Core.Messages.Delete;

namespace Microsoft.Health.Fhir.Core.Features.Resources.Delete
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1710:Identifiers should have correct suffix", Justification = "Follows validator naming convention.")]
    public class DeleteResourceValidator : AbstractValidator<DeleteResourceRequest>
    {
        public DeleteResourceValidator()
        {
            RuleFor(x => x.ResourceKey.Id)
                .SetValidator(new IdValidator());
        }
    }
}
