// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using FluentValidation.Results;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Validation.FhirPrimitiveTypes;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Validation
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Validate)]
    public class ResourceIdentityValidatorTests
    {
        private const string ExternalBase = "http://example.org/fhir/";

        [Theory]
        [InlineData(false, 64)]
        [InlineData(true, 128)]
        public void GivenARootIdAtThePolicyLimit_WhenValidating_ThenNoFailureIsReturned(bool useLongResourceIds, int length)
        {
            // Arrange
            var patient = new Patient { Id = new string('a', length) };

            // Act
            IReadOnlyList<ValidationFailure> failures = Validate(patient, ResourceIdPolicy.From(useLongResourceIds));

            // Assert
            Assert.Empty(failures);
        }

        [Theory]
        [InlineData(false, 65, "64")]
        [InlineData(true, 129, "128")]
        public void GivenARootIdOverThePolicyLimit_WhenValidating_ThenAValueFreeFailureIsReturned(bool useLongResourceIds, int length, string expectedLimit)
        {
            // Arrange
            string id = new string('a', length);
            var patient = new Patient { Id = id };

            // Act
            IReadOnlyList<ValidationFailure> failures = Validate(patient, ResourceIdPolicy.From(useLongResourceIds));

            // Assert
            ValidationFailure failure = Assert.Single(failures);
            Assert.Contains(expectedLimit, failure.ErrorMessage, StringComparison.Ordinal);
            Assert.DoesNotContain(id, failure.ErrorMessage, StringComparison.Ordinal);
        }

        [Fact]
        public void GivenAResourceWithoutAnId_WhenValidating_ThenNoFailureIsReturned()
        {
            // Act
            IReadOnlyList<ValidationFailure> failures = Validate(new Patient(), ResourceIdPolicy.Standard);

            // Assert
            Assert.Empty(failures);
        }

        [Theory]
        [InlineData("Patient/{0}")]
        [InlineData(ExternalBase + "Patient/{0}")]
        public void GivenAnIndexedReferenceOverThePolicyLimit_WhenValidating_ThenAValueFreeFailureIsReturned(string format)
        {
            // Arrange
            string id = new string('r', 65);
            var observation = new Observation { Subject = new ResourceReference(string.Format(format, id)) };

            // Act
            IReadOnlyList<ValidationFailure> failures = Validate(observation, ResourceIdPolicy.Standard);

            // Assert
            ValidationFailure failure = Assert.Single(failures);
            Assert.Contains("64", failure.ErrorMessage, StringComparison.Ordinal);
            Assert.DoesNotContain(id, failure.ErrorMessage, StringComparison.Ordinal);
            Assert.DoesNotContain(id, failure.PropertyName, StringComparison.Ordinal);
        }

        [Fact]
        public void GivenANonindexedReferenceOverThePolicyLimit_WhenValidating_ThenAFailureIsReturned()
        {
            // Arrange
            var patient = new Patient
            {
                Contact = new List<Patient.ContactComponent>
                {
                    new Patient.ContactComponent { Organization = new ResourceReference("Organization/" + new string('o', 65)) },
                },
            };

            // Act
            IReadOnlyList<ValidationFailure> failures = Validate(patient, ResourceIdPolicy.Standard);

            // Assert
            Assert.Single(failures);
        }

        [Fact]
        public void GivenAnExtensionReferenceOverThePolicyLimit_WhenValidating_ThenAFailureIsReturned()
        {
            // Arrange
            var patient = new Patient();
            patient.Extension.Add(new Extension("http://example.org/ext", new ResourceReference("Practitioner/" + new string('p', 65))));

            // Act
            IReadOnlyList<ValidationFailure> failures = Validate(patient, ResourceIdPolicy.Standard);

            // Assert
            Assert.Single(failures);
        }

        [Fact]
        public void GivenAReferenceAtTheExtendedPolicyLimit_WhenValidating_ThenNoFailureIsReturned()
        {
            // Arrange
            var observation = new Observation { Subject = new ResourceReference("Patient/" + new string('r', 128)) };

            // Act
            IReadOnlyList<ValidationFailure> failures = Validate(observation, ResourceIdPolicy.From(true));

            // Assert
            Assert.Empty(failures);
        }

        [Theory]
        [InlineData("#contained-" + "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc")]
        [InlineData("urn:uuid:5f0c52d8-6b4a-4c1d-9b0f-0123456789ab")]
        [InlineData("Patient?identifier=" + "iiiiiiiiiiiiiiiiiiiiiiiiiiiiiiiiiiiiiiiiiiiiiiiiiiiiiiiiiiiiiiiiiii")]
        [InlineData("http://example.org/not-a-rest-base/" + "uuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuu")]
        public void GivenAReferenceThatIsNotAResourceIdentity_WhenValidating_ThenNoFailureIsReturned(string reference)
        {
            // Arrange
            var observation = new Observation { Subject = new ResourceReference(reference) };

            // Act
            IReadOnlyList<ValidationFailure> failures = Validate(observation, ResourceIdPolicy.Standard);

            // Assert
            Assert.Empty(failures);
        }

        [Fact]
        public void GivenAnIdentifierOnlyReference_WhenValidating_ThenNoFailureIsReturned()
        {
            // Arrange
            var observation = new Observation
            {
                Subject = new ResourceReference { Identifier = new Identifier("http://example.org/mrn", new string('m', 200)) },
            };

            // Act
            IReadOnlyList<ValidationFailure> failures = Validate(observation, ResourceIdPolicy.Standard);

            // Assert
            Assert.Empty(failures);
        }

        [Fact]
        public void GivenTheDefaultSamples_WhenValidating_ThenNoFailureIsReturned()
        {
            // Act & Assert
            Assert.Empty(TestResourceIdentityValidators.Create().Validate(Samples.GetDefaultPatient()));
            Assert.Empty(TestResourceIdentityValidators.Create().Validate(Samples.GetDefaultObservation()));
        }

#if R4B || R5
        [Fact]
        public void GivenACodeableReferenceOverThePolicyLimit_WhenValidating_ThenAFailureIsReturned()
        {
            // Arrange
            string json = "{\"resourceType\":\"ClinicalUseDefinition\",\"type\":\"indication\",\"indication\":{\"diseaseSymptomProcedure\":{\"reference\":{\"reference\":\"Condition/"
                + new string('c', 65)
                + "\"}}}}";
            Resource resource = new FhirJsonParser().Parse<Resource>(json);

            // Act
            IReadOnlyList<ValidationFailure> failures = Validate(resource, ResourceIdPolicy.Standard);

            // Assert
            Assert.Single(failures);
        }
#endif

        private static IReadOnlyList<ValidationFailure> Validate(Resource resource, ResourceIdPolicy policy)
        {
            ResourceElement element = resource.ToResourceElement();
            return TestResourceIdentityValidators.Create(policy).Validate(element);
        }
    }
}
