// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using EnsureThat;
using Hl7.Fhir.Validation;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace Microsoft.Health.Fhir.Api.Features.Validation
{
    public class ResourceValidator : IModelValidator
    {
        public IEnumerable<ModelValidationResult> Validate(ModelValidationContext context)
        {
            EnsureArg.IsNotNull(context, nameof(context));

            var validationResults = new List<ValidationResult>();

            if (!DotNetAttributeValidation.TryValidate(context.Model, validationResults) && validationResults.Any())
            {
                return validationResults.Select(x => new ModelValidationResult(x.MemberNames.First(), $"{x.ErrorMessage}"));
            }

            return Enumerable.Empty<ModelValidationResult>();
        }
    }
}
