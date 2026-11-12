// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using FluentValidation.Results;
using Hl7.Fhir.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Resources.Create;
using Microsoft.Health.Fhir.Core.Messages.Create;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Resources.Create
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Validate)]
    public class ConditionalCreateResourceValidatorTests
    {
        private static readonly IReadOnlyList<Tuple<string, string>> ConditionalParameters = new List<Tuple<string, string>> { Tuple.Create("identifier", "mrn|1") };

        [Fact]
        public void GivenAResourceWithAnOverLengthReference_WhenValidatingConditionalCreate_ThenTheRequestIsInvalidBeforeAnyMatchCanShortCircuit()
        {
            // Arrange
            string id = new string('r', 65);
            var observation = new Observation { Status = ObservationStatus.Final, Code = new CodeableConcept("http://loinc.org", "1"), Subject = new ResourceReference("Patient/" + id) };
            var validator = new ConditionalCreateResourceValidator(NullLogger<ConditionalCreateResourceValidator>.Instance, TestResourceIdentityValidators.Create());

            // Act
            ValidationResult result = validator.Validate(new ConditionalCreateResourceRequest(observation.ToResourceElement(), ConditionalParameters));

            // Assert
            ValidationFailure failure = Assert.Single(result.Errors);
            Assert.DoesNotContain(id, failure.ErrorMessage, StringComparison.Ordinal);
        }

        [Fact]
        public void GivenAResourceWithBoundedIdentities_WhenValidatingConditionalCreate_ThenTheRequestIsValid()
        {
            // Arrange
            var observation = new Observation { Status = ObservationStatus.Final, Code = new CodeableConcept("http://loinc.org", "1"), Subject = new ResourceReference("Patient/" + new string('r', 64)) };
            var validator = new ConditionalCreateResourceValidator(NullLogger<ConditionalCreateResourceValidator>.Instance, TestResourceIdentityValidators.Create());

            // Act
            ValidationResult result = validator.Validate(new ConditionalCreateResourceRequest(observation.ToResourceElement(), ConditionalParameters));

            // Assert
            Assert.True(result.IsValid);
        }
    }
}
