// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using FluentValidation.Results;
using FluentValidation.Validators;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Extensions;
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
                var poco = resourceElement.ToPoco();

                if (poco is DomainResource resource)
                {
                    foreach (ValidationFailure validationFailure in ValidateResource(resource))
                    {
                        yield return validationFailure;
                    }
                }

                if (poco is Bundle bundle)
                {
                    var domainResources = bundle.Entry.Select(x => x.Resource).OfType<DomainResource>();

                    foreach (ValidationFailure validationFailure in domainResources.SelectMany(ValidateResource))
                    {
                        yield return validationFailure;
                    }
                }
            }
        }

        private IEnumerable<ValidationFailure> ValidateResource(DomainResource domainResource)
        {
            EnsureArg.IsNotNull(domainResource, nameof(domainResource));

            if (string.IsNullOrEmpty(domainResource.Text?.Div))
            {
                yield break;
            }

            var errors = _narrativeHtmlSanitizer.Validate(domainResource.Text.Div);
            string propertyName = $"{domainResource.TypeName}.Text.Div";

            foreach (var error in errors)
            {
                yield return new FhirValidationFailure(
                    propertyName,
                    error,
                    new OperationOutcomeIssue(
                        OperationOutcomeConstants.IssueType.Structure,
                        OperationOutcomeConstants.IssueSeverity.Error,
                        error,
                        location: new[] { propertyName }));
            }
        }
    }
}
