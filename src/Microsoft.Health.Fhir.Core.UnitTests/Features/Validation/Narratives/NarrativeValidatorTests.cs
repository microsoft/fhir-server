// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using FluentValidation;
using FluentValidation.Internal;
using FluentValidation.Validators;
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
        public void GivenAnInvalidNarrative_WhenProcessingAResource_ThenAValidationMessageIsCreated(string maliciousNarrative)
        {
            var defaultObservation = Samples.GetDefaultObservation().ToPoco<Observation>();
            defaultObservation.Text.Div = maliciousNarrative;

            var result = _validator.Validate(
                new PropertyValidatorContext(
                    new ValidationContext(defaultObservation.ToResourceElement()),
                    PropertyRule.Create<ResourceElement, ResourceElement>(x => x),
                    "Resource"));

            Assert.NotEmpty(result);
        }

        [Theory]
        [MemberData(nameof(XssStrings))]
        public void GivenAnInvalidNarrative_WhenProcessingABundle_ThenAValidationMessageIsCreated(string maliciousNarrative)
        {
            var defaultObservation = Samples.GetDefaultObservation().ToPoco<Observation>();
            defaultObservation.Text.Div = maliciousNarrative;

            var defaultPatient = Samples.GetDefaultPatient().ToPoco<Patient>();
            defaultPatient.Text.Div = maliciousNarrative;

            var bundle = new Bundle();
            bundle.Entry.Add(new Bundle.EntryComponent { Resource = defaultObservation });
            bundle.Entry.Add(new Bundle.EntryComponent { Resource = defaultPatient });

            var result = _validator.Validate(
                new PropertyValidatorContext(
                    new ValidationContext(bundle.ToResourceElement()),
                    PropertyRule.Create<ResourceElement, ResourceElement>(x => x),
                    "Resource"));

            Assert.NotEmpty(result);
        }
    }
}
