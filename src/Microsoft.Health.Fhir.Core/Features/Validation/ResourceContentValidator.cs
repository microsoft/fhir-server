// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using FluentValidation;
using FluentValidation.Results;
using FluentValidation.Validators;
using Microsoft.Health.Fhir.Core.Models;
using ValidationResult = System.ComponentModel.DataAnnotations.ValidationResult;

namespace Microsoft.Health.Fhir.Core.Features.Validation
{
    /// <summary>
    /// Validates content of resource.
    /// </summary>
    /// <typeparam name="T">The type of the element.</typeparam>
    /// <remarks>
    /// Even if we correctly parsed resource into object it doesn't mean resource is valid.
    /// We need to check that properties have right cardinality, correct types, proper format, etc.
    /// </remarks>
    public class ResourceContentValidator : NoopPropertyValidator<ResourceElement, ResourceElement>
    {
        private readonly IModelAttributeValidator _modelAttributeValidator;

        public ResourceContentValidator(IModelAttributeValidator modelAttributeValidator)
        {
            EnsureArg.IsNotNull(modelAttributeValidator, nameof(modelAttributeValidator));

            _modelAttributeValidator = modelAttributeValidator;
        }

        public override string Name => nameof(ResourceContentValidator);

        public override bool IsValid(ValidationContext<ResourceElement> context, ResourceElement value)
        {
            EnsureArg.IsNotNull(context, nameof(context));
            bool isValid = true;
            if (context.InstanceToValidate is ResourceElement resourceElement)
            {
                var results = new List<ValidationResult>();
                if (!_modelAttributeValidator.TryValidate(resourceElement, results, false))
                {
                    foreach (var error in results)
                    {
                        var fullFhirPath = resourceElement.InstanceType;
                        fullFhirPath += string.IsNullOrEmpty(error.MemberNames?.FirstOrDefault()) ? string.Empty : "." + error.MemberNames?.FirstOrDefault();
                        var validationFailure = new ValidationFailure(fullFhirPath, error.ErrorMessage);
                        validationFailure.ErrorCode = "Custom";
                        context.AddFailure(validationFailure);
                        isValid = false;
                    }
                }
            }

            return isValid;
        }
    }
}
