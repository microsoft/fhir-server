// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Models;
using ValidationResult = System.ComponentModel.DataAnnotations.ValidationResult;

namespace Microsoft.Health.Fhir.Core.Features.Validation
{
    /// <summary>
    /// Validates content of resource.
    /// </summary>
    /// <remarks>
    /// Even if we correctly parsed resource into object it doesn't mean resource is valid.
    /// We need to check that properties have right cardinality, correct types, proper format, etc.
    /// </remarks>
    public class ResourceContentValidator : AbstractValidator<ResourceElement>
    {
        private readonly IModelAttributeValidator _modelAttributeValidator;
        private readonly ILogger _logger;

        public ResourceContentValidator(IModelAttributeValidator modelAttributeValidator, ILogger logger)
        {
            EnsureArg.IsNotNull(modelAttributeValidator, nameof(modelAttributeValidator));

            _modelAttributeValidator = modelAttributeValidator;
            _logger = logger;
        }

        public override Task<FluentValidation.Results.ValidationResult> ValidateAsync(ValidationContext<ResourceElement> context, CancellationToken cancellation = default)
        {
            return Task.FromResult(Validate(context));
        }

        public override FluentValidation.Results.ValidationResult Validate(ValidationContext<ResourceElement> context)
        {
            EnsureArg.IsNotNull(context, nameof(context));
            var timer = _logger.StartStopwatch("Resource Content Validator");

            var failures = new List<ValidationFailure>();
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
                        failures.Add(validationFailure);
                    }
                }
            }

            failures.ForEach(x => context.AddFailure(x));

            _logger.LogStopwatch(timer, "End");
            return new FluentValidation.Results.ValidationResult(failures);
        }
    }
}
