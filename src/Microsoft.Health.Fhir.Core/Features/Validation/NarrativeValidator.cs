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
using Hl7.Fhir.ElementModel;
using Hl7.FhirPath;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Validation.Narratives
{
    public class NarrativeValidator : AbstractValidator<ResourceElement>
    {
        private readonly INarrativeHtmlSanitizer _narrativeHtmlSanitizer;
        private readonly ILogger<NarrativeValidator> _logger;

        public NarrativeValidator(INarrativeHtmlSanitizer narrativeHtmlSanitizer, ILogger<NarrativeValidator> logger)
        {
            EnsureArg.IsNotNull(narrativeHtmlSanitizer, nameof(narrativeHtmlSanitizer));

            _narrativeHtmlSanitizer = narrativeHtmlSanitizer;
            _logger = logger;
        }

        public override Task<ValidationResult> ValidateAsync(ValidationContext<ResourceElement> context, CancellationToken cancellation = default)
        {
            return Task.FromResult(Validate(context));
        }

        public override ValidationResult Validate(ValidationContext<ResourceElement> context)
        {
            EnsureArg.IsNotNull(context, nameof(context));
            var timer = _logger.StartStopwatch("Narative Validator");

            var failures = new List<ValidationFailure>();
            if (context.InstanceToValidate is ResourceElement resourceElement)
            {
                if (resourceElement.IsDomainResource)
                {
                    _logger.LogInformation("Domain resource");
                    failures.AddRange(ValidateResource(resourceElement.Instance));
                }
                else if (resourceElement.InstanceType.Equals(KnownResourceTypes.Bundle, System.StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation("Bundle");
                    var bundleEntries = resourceElement.Instance.Select(KnownFhirPaths.BundleEntries);
                    if (bundleEntries != null)
                    {
                        failures.AddRange(bundleEntries.SelectMany(ValidateResource));
                    }
                }
            }

            failures.ForEach(x => context.AddFailure(x));

            _logger.LogStopwatch(timer, "End");
            return new ValidationResult(failures);
        }

        private IEnumerable<ValidationFailure> ValidateResource(ITypedElement typedElement)
        {
            EnsureArg.IsNotNull(typedElement, nameof(typedElement));

            var xhtml = typedElement.Scalar(KnownFhirPaths.ResourceNarrative) as string;
            if (string.IsNullOrEmpty(xhtml))
            {
                yield break;
            }

            var errors = _narrativeHtmlSanitizer.Validate(xhtml);
            var fullFhirPath = typedElement.InstanceType + "." + KnownFhirPaths.ResourceNarrative;

            foreach (var error in errors)
            {
                yield return new FhirValidationFailure(
                    fullFhirPath,
                    error,
                    new OperationOutcomeIssue(
                        OperationOutcomeConstants.IssueSeverity.Error,
                        OperationOutcomeConstants.IssueType.Structure,
                        error,
                        expression: new[] { fullFhirPath }));
            }
        }
    }
}
