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
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Models;
using static Microsoft.Health.Fhir.Core.Models.OperationOutcomeConstants;

namespace Microsoft.Health.Fhir.Core.Features.Validation
{
    public sealed class ResourceProfileValidator : ResourceContentValidator
    {
        private readonly IProfileValidator _profileValidator;
        private readonly RequestContextAccessor<IFhirRequestContext> _contextAccessor;
        private readonly bool _runProfileValidation;
        private readonly ILogger _logger;

        public ResourceProfileValidator(
            IModelAttributeValidator modelAttributeValidator,
            IProfileValidator profileValidator,
            RequestContextAccessor<IFhirRequestContext> contextAccessor,
            ILogger logger,
            bool runProfileValidation = false)
            : base(modelAttributeValidator, logger)
        {
            EnsureArg.IsNotNull(modelAttributeValidator, nameof(modelAttributeValidator));
            EnsureArg.IsNotNull(profileValidator, nameof(profileValidator));
            EnsureArg.IsNotNull(contextAccessor, nameof(contextAccessor));

            _profileValidator = profileValidator;
            _contextAccessor = contextAccessor;
            _runProfileValidation = runProfileValidation;
            _logger = logger;
        }

        public override Task<ValidationResult> ValidateAsync(ValidationContext<ResourceElement> context, CancellationToken cancellation = default)
        {
            return Task.FromResult(Validate(context));
        }

        public override ValidationResult Validate(ValidationContext<ResourceElement> context)
        {
            EnsureArg.IsNotNull(context, nameof(context));
            var timer = _logger.StartStopwatch("Resource Profile Validator");

            var failures = new List<ValidationFailure>();
            if (context.InstanceToValidate is ResourceElement resourceElement)
            {
                var fhirContext = _contextAccessor.RequestContext;
                var profileValidation = _runProfileValidation;
                if (fhirContext.RequestHeaders.ContainsKey(KnownHeaders.ProfileValidation)
                    && fhirContext.RequestHeaders.TryGetValue(KnownHeaders.ProfileValidation, out var hValue))
                {
                    if (bool.TryParse(hValue, out bool headerValue))
                    {
                        _logger.LogInformation("Profile set");
                        profileValidation = headerValue;
                    }
                }

                var isStrict = _contextAccessor.GetIsStrictHandlingEnabled();

                if (profileValidation)
                {
                    _logger.LogStopwatch(timer, "Validating profile");
                    OperationOutcomeIssue[] errors = _profileValidator.TryValidate(resourceElement.Instance);
                    _logger.LogStopwatch(timer, "Profile validated");

                    foreach (OperationOutcomeIssue error in errors
                                 .Where(x => x.Severity == IssueSeverity.Error || x.Severity == IssueSeverity.Fatal || (isStrict && x.Severity == IssueSeverity.Warning)))
                    {
                        var validationFailure = new FhirValidationFailure(
                            resourceElement.InstanceType,
                            error.DetailsText,
                            error);
                        failures.Add(validationFailure);
                    }

                    failures.ForEach(x => context.AddFailure(x));
                }

                _logger.LogStopwatch(timer, "Base validation");
                ValidationResult baseValidation = base.Validate(context);
                failures.AddRange(baseValidation.Errors);
            }

            _logger.LogStopwatch(timer, "End");
            return new ValidationResult(failures);
        }
    }
}
