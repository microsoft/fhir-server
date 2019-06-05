// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using FluentValidation.Results;
using FluentValidation.Validators;
using Hl7.FhirPath;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Validation.Narratives
{
    public class NarrativeValidator : NoopPropertyValidator
    {
        private readonly INarrativeHtmlSanitizer _narrativeHtmlSanitizer;

        public NarrativeValidator(INarrativeHtmlSanitizer narrativeHtmlSanitizer)
        {
            EnsureArg.IsNotNull(narrativeHtmlSanitizer, nameof(narrativeHtmlSanitizer));

            _narrativeHtmlSanitizer = narrativeHtmlSanitizer;
        }

        public override IEnumerable<ValidationFailure> Validate(PropertyValidatorContext context)
        {
            EnsureArg.IsNotNull(context, nameof(context));

            if (context.PropertyValue is ResourceElement resourceElement)
            {
                if (resourceElement.IsDomainResource)
                {
                    foreach (ValidationFailure validationFailure in ValidateResource(resourceElement))
                    {
                        yield return validationFailure;
                    }
                }
                else if (resourceElement.InstanceType.Equals("Bundle", System.StringComparison.OrdinalIgnoreCase))
                {
                    var bundleEntries = resourceElement.Instance.Select("Bundle.entry.resource");
                    if (bundleEntries != null)
                    {
                        var domainResources = bundleEntries.Select(e => new ResourceElement(e)).Where(r => r.IsDomainResource);
                        foreach (ValidationFailure validationFailure in domainResources.SelectMany(ValidateResource))
                        {
                            yield return validationFailure;
                        }
                    }
                }
            }
        }

        private IEnumerable<ValidationFailure> ValidateResource(ResourceElement domainResource)
        {
            EnsureArg.IsNotNull(domainResource, nameof(domainResource));

            const string fhirPath = "text.div";

            var xhtml = domainResource.Scalar<string>(fhirPath);
            if (string.IsNullOrEmpty(xhtml))
            {
                yield break;
            }

            var errors = _narrativeHtmlSanitizer.Validate(xhtml);
            var fullFhirPath = domainResource.InstanceType + "." + fhirPath;

            foreach (var error in errors)
            {
                yield return new FhirValidationFailure(
                    fullFhirPath,
                    error,
                    new OperationOutcomeIssue(
                        OperationOutcomeConstants.IssueType.Structure,
                        OperationOutcomeConstants.IssueSeverity.Error,
                        error,
                        location: new[] { fullFhirPath }));
            }
        }
    }
}
