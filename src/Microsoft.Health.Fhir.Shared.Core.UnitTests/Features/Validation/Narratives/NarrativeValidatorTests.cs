// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using FluentValidation;
using FluentValidation.Internal;
using FluentValidation.Results;
using FluentValidation.Validators;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Validation.Narratives;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Validation.Narratives
{
    public class NarrativeValidatorTests : NarrativeDataTestBase
    {
        private readonly NarrativeValidator _validator;

        public NarrativeValidatorTests()
        {
            _validator = new NarrativeValidator(new NarrativeHtmlSanitizer(NullLogger<NarrativeHtmlSanitizer>.Instance));
        }

        [Theory]
        [MemberData(nameof(XssStrings))]
        public void GivenAnInvalidNarrative_WhenProcessingAResource_ThenAValidationMessageWithAFhirPathIsCreated(string maliciousNarrative)
        {
            var defaultObservation = Samples.GetDefaultObservation().ToPoco<Observation>();
            defaultObservation.Text.Div = maliciousNarrative;

            var instanceToValidate = defaultObservation.ToResourceElement();

            IEnumerable<ValidationFailure> result = _validator.Validate(
                new PropertyValidatorContext(
                    new ValidationContext<ResourceElement>(instanceToValidate),
                    PropertyRule.Create<ResourceElement, ResourceElement>(x => x),
                    "Resource",
                    instanceToValidate));

            List<ValidationFailure> validationFailures = result as List<ValidationFailure> ?? result.ToList();
            Assert.NotEmpty(validationFailures);

            var actualFhirPath = validationFailures.FirstOrDefault()?.PropertyName;
            var expectedFhirPath = defaultObservation.TypeName + "." + KnownFhirPaths.ResourceNarrative;

            Assert.Equal(expectedFhirPath, actualFhirPath);
        }

        [Theory]
        [MemberData(nameof(XssStrings))]
        public void GivenAnInvalidNarrative_WhenProcessingABundle_ThenAValidationMessageWithAFhirPathIsCreated(string maliciousNarrative)
        {
            var defaultObservation = Samples.GetDefaultObservation().ToPoco<Observation>();
            defaultObservation.Text.Div = maliciousNarrative;

            var defaultPatient = Samples.GetDefaultPatient().ToPoco<Patient>();
            defaultPatient.Text.Div = maliciousNarrative;

            var bundle = new Bundle();
            bundle.Entry.Add(new Bundle.EntryComponent { Resource = defaultObservation });
            bundle.Entry.Add(new Bundle.EntryComponent { Resource = defaultPatient });

            var instanceToValidate = bundle.ToResourceElement();
            var result = _validator.Validate(
                new PropertyValidatorContext(
                    new ValidationContext<ResourceElement>(instanceToValidate),
                    PropertyRule.Create<ResourceElement, ResourceElement>(x => x),
                    "Resource",
                    instanceToValidate));

            List<ValidationFailure> validationFailures = result as List<ValidationFailure> ?? result.ToList();
            Assert.NotEmpty(validationFailures);

            var expectedObservationFhirPath = defaultObservation.TypeName + "." + KnownFhirPaths.ResourceNarrative;
            var expectedPatientFhirPath = defaultPatient.TypeName + "." + KnownFhirPaths.ResourceNarrative;

            Assert.Equal(expectedObservationFhirPath, validationFailures[0].PropertyName);
            Assert.Equal(expectedPatientFhirPath, validationFailures[1].PropertyName);
        }
    }
}
