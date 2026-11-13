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
using Hl7.Fhir.Validation;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Context;
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
        private readonly RequestContextAccessor<IFhirRequestContext> _contextAccessor;

        public ResourceContentValidator(IModelAttributeValidator modelAttributeValidator)
            : this(modelAttributeValidator, null)
        {
        }

        public ResourceContentValidator(
            IModelAttributeValidator modelAttributeValidator,
            RequestContextAccessor<IFhirRequestContext> contextAccessor)
        {
            EnsureArg.IsNotNull(modelAttributeValidator, nameof(modelAttributeValidator));

            _modelAttributeValidator = modelAttributeValidator;
            _contextAccessor = contextAccessor;
        }

        public override Task<FluentValidation.Results.ValidationResult> ValidateAsync(ValidationContext<ResourceElement> context, CancellationToken cancellation = default)
        {
            return Task.FromResult(Validate(context));
        }

        public override FluentValidation.Results.ValidationResult Validate(ValidationContext<ResourceElement> context)
        {
            EnsureArg.IsNotNull(context, nameof(context));
            var failures = new List<ValidationFailure>();
            if (context.InstanceToValidate is ResourceElement resourceElement)
            {
                bool recurse = GetRecursiveValidationSetting();

                var results = new List<ValidationResult>();
                if (!_modelAttributeValidator.TryValidate(resourceElement, results, recurse))
                {
                    foreach (var error in results)
                    {
                        string fullFhirPath;

                        if (error is CodedValidationResult codedValidationResult)
                        {
                            fullFhirPath = codedValidationResult.ValidationException.InstancePath;
                        }
                        else
                        {
                            fullFhirPath = resourceElement.InstanceType;
                            fullFhirPath += string.IsNullOrEmpty(error.MemberNames?.FirstOrDefault()) ? string.Empty : "." + error.MemberNames?.FirstOrDefault();
                        }

                        var validationFailure = new ValidationFailure(fullFhirPath, error.ErrorMessage);
                        failures.Add(validationFailure);
                    }
                }
            }

            failures.ForEach(x => context.AddFailure(x));
            return new FluentValidation.Results.ValidationResult(failures);
        }

        /// <summary>
        /// Gets the recursive validation setting from the request header.
        /// Defaults to false if the header is not present or invalid.
        /// </summary>
        /// <returns>True if recursive validation is enabled; otherwise, false.</returns>
        private bool GetRecursiveValidationSetting()
        {
            if (_contextAccessor?.RequestContext?.RequestHeaders == null)
            {
                return false;
            }

            var fhirContext = _contextAccessor.RequestContext;
            if (fhirContext.RequestHeaders.ContainsKey(KnownHeaders.RecursiveValidation)
                && fhirContext.RequestHeaders.TryGetValue(KnownHeaders.RecursiveValidation, out var headerValue))
            {
                if (bool.TryParse(headerValue, out bool recursiveValidation))
                {
                    return recursiveValidation;
                }
            }

            return false;
        }
    }
}
