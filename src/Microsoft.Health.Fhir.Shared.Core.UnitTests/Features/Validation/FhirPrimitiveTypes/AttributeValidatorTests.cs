// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Collections.Generic;
using System.Linq;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Validation;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;
using ValidationResult = System.ComponentModel.DataAnnotations.ValidationResult;

namespace Microsoft.Health.Fhir.Shared.Core.UnitTests.Features.Validation.FhirPrimitiveTypes
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Validate)]
    public class AttributeValidatorTests
    {
        private IModelAttributeValidator _modelAttributeValidator;

        public AttributeValidatorTests()
        {
            _modelAttributeValidator = new ModelAttributeValidator();
        }

        [Fact]
        public void GivenAMissingAttribute_WhenProcessingAResource_ThenAValidationMessageWithAFhirPathIsCreated()
        {
            var defaultObservation = Samples.GetDefaultObservation().ToPoco<Observation>();
            defaultObservation.StatusElement = null;

            var results = new List<ValidationResult>();
            bool isValid = _modelAttributeValidator.TryValidate(defaultObservation.ToResourceElement(), results, recurse: false);

            Assert.False(isValid);

            List<ValidationResult> validationFailures = results ?? results.ToList();
            Assert.Single(validationFailures);

            var error = validationFailures.FirstOrDefault();
            var actualPartialFhirPath = string.IsNullOrEmpty(error.MemberNames?.FirstOrDefault()) ? string.Empty : error.MemberNames?.FirstOrDefault();

            // TODO: Expected value should be "status" once actual path is fixed.
            Assert.Equal("StatusElement", actualPartialFhirPath);
        }

        [Fact]
        public void GivenObservationWithoutRequiredStatus_WhenValidating_ThenValidationShouldFail()
        {
            // Test required field validation (1..1 cardinality)
            var observation = new Observation
            {
                Code = new CodeableConcept("http://loinc.org", "15074-8"),

                // Missing required 'status' field
            };

            var results = new List<ValidationResult>();
            bool isValid = _modelAttributeValidator.TryValidate(observation.ToResourceElement(), results, recurse: false);

            Assert.False(isValid);
            Assert.Contains(results, r => r.ErrorMessage.Contains("StatusElement") || r.ErrorMessage.Contains("minimum cardinality 1"));
        }

        [Fact]
        public void GivenPatientWithMultipleIdentifiers_WhenValidating_ThenCardinalityShouldBeRespected()
        {
            // Test 0..* cardinality (zero or more allowed)
            var patient = new Patient
            {
                Identifier = new List<Identifier>
                {
                    new Identifier("http://hospital.org/mrn", "12345"),
                    new Identifier("http://national-id.org", "987654"),
                    new Identifier("http://insurance.org", "INS-001"),
                },
            };

            var results = new List<ValidationResult>();
            bool isValid = _modelAttributeValidator.TryValidate(patient.ToResourceElement(), results, recurse: false);

            Assert.True(isValid);
        }

        [Fact]
        public void GivenObservationWithChoiceTypeValue_WhenValidating_ThenChoiceTypeShouldBeValid()
        {
            // Test choice type validation (value[x])
            var observation = new Observation
            {
                Status = ObservationStatus.Final,
                Code = new CodeableConcept("http://loinc.org", "29463-7", "Body weight"),
                Value = new Quantity
                {
                    Value = 85.5m,
                    Unit = "kg",
                    System = "http://unitsofmeasure.org",
                    Code = "kg",
                },
            };

            var results = new List<ValidationResult>();
            bool isValid = _modelAttributeValidator.TryValidate(observation.ToResourceElement(), results, recurse: false);

            Assert.True(isValid);
        }

        [Fact]
        public void GivenDiagnosticReportWithRequiredStatus_WhenValidating_ThenValidationShouldSucceed()
        {
            // Test 1..1 cardinality (required, max 1)
            var diagnosticReport = new DiagnosticReport
            {
                Status = DiagnosticReport.DiagnosticReportStatus.Final,
                Code = new CodeableConcept("http://loinc.org", "58410-2", "Complete blood count"),
            };

            var results = new List<ValidationResult>();
            bool isValid = _modelAttributeValidator.TryValidate(diagnosticReport.ToResourceElement(), results, recurse: false);

            Assert.True(isValid);
        }

        [Fact]
        public void GivenAllergyIntoleranceWithOptionalOnset_WhenValidating_ThenCardinalityShouldAllow0Or1()
        {
            // Test 0..1 cardinality (optional, max 1)
            var allergyIntolerance = new AllergyIntolerance
            {
                Patient = new ResourceReference("Patient/123"),

                // OnsetDateTime is optional (0..1)
                Onset = new FhirDateTime("2023-01-15"),
            };

            var results = new List<ValidationResult>();
            bool isValid = _modelAttributeValidator.TryValidate(allergyIntolerance.ToResourceElement(), results, recurse: false);

            Assert.True(isValid);
        }

        [Fact]
        public void GivenQuestionnaireResponseWithNestedAnswers_WhenValidating_ThenRecursiveStructureShouldBeValid()
        {
            // Test recursive/nested structures
            var questionnaireResponse = new QuestionnaireResponse
            {
                Status = QuestionnaireResponse.QuestionnaireResponseStatus.Completed,
                Item = new List<QuestionnaireResponse.ItemComponent>
                {
                    new QuestionnaireResponse.ItemComponent
                    {
                        LinkId = "1",
                        Answer = new List<QuestionnaireResponse.AnswerComponent>
                        {
                            new QuestionnaireResponse.AnswerComponent
                            {
                                Value = new FhirString("Yes"),
                            },
                        },
                    },
                },
            };

            var results = new List<ValidationResult>();
            bool isValid = _modelAttributeValidator.TryValidate(questionnaireResponse.ToResourceElement(), results, recurse: false);

            Assert.True(isValid);
        }
    }
}
