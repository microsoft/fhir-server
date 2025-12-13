// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Hl7.Fhir.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Resources.Upsert;
using Microsoft.Health.Fhir.Core.Messages.Upsert;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Resources.Upsert
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Validate)]
    public class ConditionalUpsertResourceValidatorTests
    {
        private readonly ILogger<ConditionalUpsertResourceValidator> _logger;
        private readonly ConditionalUpsertResourceValidator _validator;

        public ConditionalUpsertResourceValidatorTests()
        {
            _logger = Substitute.For<ILogger<ConditionalUpsertResourceValidator>>();
            _validator = new ConditionalUpsertResourceValidator(_logger);
        }

        [Fact]
        public void GivenEmptyConditionalParameters_WhenValidating_ThenValidationShouldFail()
        {
            // Arrange
            var patient = CreateTestPatient();
            var emptyParameters = new List<Tuple<string, string>>();
            var request = new ConditionalUpsertResourceRequest(patient.ToResourceElement(), emptyParameters);

            // Act
            var result = _validator.Validate(request);

            // Assert
            Assert.False(result.IsValid);
            Assert.Single(result.Errors);
            Assert.Contains("not selective enough", result.Errors[0].ErrorMessage);
        }

        [Fact]
        public void GivenEmptyConditionalParameters_WhenValidating_ThenLoggerShouldBeInvoked()
        {
            // Arrange
            var patient = CreateTestPatient();
            var emptyParameters = new List<Tuple<string, string>>();
            var request = new ConditionalUpsertResourceRequest(patient.ToResourceElement(), emptyParameters);

            // Act
            _validator.Validate(request);

            // Assert
            _logger.Received(1).Log(
                LogLevel.Information,
                Arg.Any<EventId>(),
                Arg.Is<object>(o => o.ToString().Contains("PreconditionFailed: ConditionalOperationNotSelectiveEnough")),
                null,
                Arg.Any<Func<object, Exception, string>>());
        }

        [Fact]
        public void GivenSingleConditionalParameter_WhenValidating_ThenValidationShouldSucceed()
        {
            // Arrange
            var patient = CreateTestPatient();
            var conditionalParameters = new List<Tuple<string, string>>
            {
                new Tuple<string, string>("name", "John"),
            };
            var request = new ConditionalUpsertResourceRequest(patient.ToResourceElement(), conditionalParameters);

            // Act
            var result = _validator.Validate(request);

            // Assert
            Assert.True(result.IsValid);
            Assert.Empty(result.Errors);
        }

        [Fact]
        public void GivenSingleConditionalParameter_WhenValidating_ThenLoggerShouldNotBeInvoked()
        {
            // Arrange
            var patient = CreateTestPatient();
            var conditionalParameters = new List<Tuple<string, string>>
            {
                new Tuple<string, string>("name", "John"),
            };
            var request = new ConditionalUpsertResourceRequest(patient.ToResourceElement(), conditionalParameters);

            // Act
            _validator.Validate(request);

            // Assert
            _logger.DidNotReceive().Log(
                LogLevel.Information,
                Arg.Any<EventId>(),
                Arg.Any<object>(),
                null,
                Arg.Any<Func<object, Exception, string>>());
        }

        [Fact]
        public void GivenMultipleConditionalParameters_WhenValidating_ThenValidationShouldSucceed()
        {
            // Arrange
            var patient = CreateTestPatient();
            var conditionalParameters = new List<Tuple<string, string>>
            {
                new Tuple<string, string>("name", "John"),
                new Tuple<string, string>("birthdate", "1970-01-01"),
                new Tuple<string, string>("gender", "male"),
            };
            var request = new ConditionalUpsertResourceRequest(patient.ToResourceElement(), conditionalParameters);

            // Act
            var result = _validator.Validate(request);

            // Assert
            Assert.True(result.IsValid);
            Assert.Empty(result.Errors);
        }

        [Fact]
        public void GivenEmptyConditionalParameters_WhenValidating_ThenErrorMessageShouldContainResourceType()
        {
            // Arrange
            var patient = CreateTestPatient();
            var emptyParameters = new List<Tuple<string, string>>();
            var request = new ConditionalUpsertResourceRequest(patient.ToResourceElement(), emptyParameters);

            // Act
            var result = _validator.Validate(request);

            // Assert
            Assert.False(result.IsValid);
            Assert.Single(result.Errors);
            Assert.Contains("Patient", result.Errors[0].ErrorMessage);
        }

        [Fact]
        public void GivenDifferentResourceType_WhenValidatingWithEmptyParameters_ThenErrorShouldContainCorrectResourceType()
        {
            // Arrange
            var observation = CreateTestObservation();
            var emptyParameters = new List<Tuple<string, string>>();
            var request = new ConditionalUpsertResourceRequest(observation.ToResourceElement(), emptyParameters);

            // Act
            var result = _validator.Validate(request);

            // Assert
            Assert.False(result.IsValid);
            Assert.Single(result.Errors);
            Assert.Contains("Observation", result.Errors[0].ErrorMessage);
            Assert.DoesNotContain("Patient", result.Errors[0].ErrorMessage);
        }

        [Fact]
        public void GivenNullLogger_WhenValidatingWithEmptyParameters_ThenValidationShouldStillWork()
        {
            // Arrange
            var validatorWithNullLogger = new ConditionalUpsertResourceValidator(null);
            var patient = CreateTestPatient();
            var emptyParameters = new List<Tuple<string, string>>();
            var request = new ConditionalUpsertResourceRequest(patient.ToResourceElement(), emptyParameters);

            // Act
            var result = validatorWithNullLogger.Validate(request);

            // Assert
            Assert.False(result.IsValid);
            Assert.Single(result.Errors);
        }

        [Fact]
        public void GivenEmptyConditionalParameters_WhenValidating_ThenErrorPropertyNameShouldBeConditionalParameters()
        {
            // Arrange
            var patient = CreateTestPatient();
            var emptyParameters = new List<Tuple<string, string>>();
            var request = new ConditionalUpsertResourceRequest(patient.ToResourceElement(), emptyParameters);

            // Act
            var result = _validator.Validate(request);

            // Assert
            Assert.False(result.IsValid);
            Assert.Single(result.Errors);
            Assert.Equal("ConditionalParameters", result.Errors[0].PropertyName);
        }

        private static Patient CreateTestPatient()
        {
            return new Patient
            {
                Id = "test-patient",
                Name = new List<HumanName>
                {
                    new HumanName
                    {
                        Family = "Doe",
                        Given = new[] { "John" },
                    },
                },
                Gender = AdministrativeGender.Male,
                BirthDate = "1970-01-01",
            };
        }

        private static Observation CreateTestObservation()
        {
            return new Observation
            {
                Id = "test-observation",
                Status = ObservationStatus.Final,
                Code = new CodeableConcept
                {
                    Coding = new List<Coding>
                    {
                        new Coding("http://loinc.org", "85354-9", "Blood pressure"),
                    },
                },
            };
        }
    }
}
