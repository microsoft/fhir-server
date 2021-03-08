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
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Validation
{
    public sealed class ResourceProfileValidator : ResourceContentValidator
    {
        private readonly IProfileValidator _profileValidator;
        private readonly IFhirRequestContextAccessor _contextAccessor;
        private readonly bool _runProfileValidation;

        public ResourceProfileValidator(
            IModelAttributeValidator modelAttributeValidator,
            IProfileValidator profileValidator,
            IFhirRequestContextAccessor contextAccessor,
            bool runProfileValidation = false)
            : base(modelAttributeValidator)
        {
            EnsureArg.IsNotNull(modelAttributeValidator, nameof(modelAttributeValidator));
            EnsureArg.IsNotNull(profileValidator, nameof(profileValidator));
            EnsureArg.IsNotNull(contextAccessor, nameof(contextAccessor));

            _profileValidator = profileValidator;
            _contextAccessor = contextAccessor;
            _runProfileValidation = runProfileValidation;
        }

        public override IEnumerable<ValidationFailure> Validate(PropertyValidatorContext context)
        {
            EnsureArg.IsNotNull(context, nameof(context));

            if (context.PropertyValue is ResourceElement resourceElement)
            {
                var results = new List<ValidationResult>();

                var fhirContext = _contextAccessor.FhirRequestContext;
                var profileValidation = _runProfileValidation;
                if (fhirContext.RequestHeaders.ContainsKey(KnownHeaders.ProfileValidation)
                    && fhirContext.RequestHeaders.TryGetValue(KnownHeaders.ProfileValidation, out var value))
                {
                    if (bool.TryParse(value, out bool headerValue))
                    {
                        profileValidation = headerValue;
                    }
                }

                if (profileValidation)
                {
                    var typedElemat = resourceElement.Instance;
                    var errors = _profileValidator.TryValidate(resourceElement.Instance);
                    var fullFhirPath = resourceElement.InstanceType;

                    foreach (var error in errors.Where(x => x.Severity == Severity.Error.ToString()))
                    {
                        yield return new FhirValidationFailure(
                            fullFhirPath,
                            error.DetailsText,
                            error);
                    }
                }

                foreach (var baseError in base.Validate(context))
                {
                    yield return baseError;
                }
            }
        }
    }
}
