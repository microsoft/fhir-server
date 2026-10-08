// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using FluentValidation;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Validation.FhirPrimitiveTypes;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Validation.FhirPrimitiveTypes
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Validate)]
    public class IdValidatorTests
    {
        [Theory]
        [InlineData("1+1")]
        [InlineData("1_1")]
        [InlineData("11|")]
        [InlineData("00000000000000000000000000000000000000000000000000000000000000065")]
        public void GivenAnInvalidId_WhenProcessingAResource_ThenAValidationMessageWithAFhirPathIsCreated(string id)
        {
            var defaultObservation = Samples.GetDefaultObservation().UpdateId(id);

            var result = GetValidationFailures(defaultObservation, ResourceIdPolicy.Standard);

            Assert.False(result);
        }

        [Theory]
        [InlineData("1.1")]
        [InlineData("id1")]
        [InlineData("example")]
        [InlineData("a94060e6-038e-411b-a64b-38c2c3ff0fb7")]
        [InlineData("AF30C45C-94AC-4DE3-89D8-9A20BB2A973F")]
        [InlineData("0000000000000000000000000000000000000000000000000000000000000064")]
        public void GivenAValidId_WhenProcessingAResource_ThenAValidationMessageIsNotCreated(string id)
        {
            var defaultObservation = Samples.GetDefaultObservation().UpdateId(id);

            var result = GetValidationFailures(defaultObservation, ResourceIdPolicy.Standard);

            Assert.True(result);
        }

        [Theory]
        [InlineData(false, 64, true)]
        [InlineData(false, 65, false)]
        [InlineData(false, 128, false)]
        [InlineData(true, 64, true)]
        [InlineData(true, 65, true)]
        [InlineData(true, 128, true)]
        [InlineData(true, 129, false)]
        public void GivenAnId_WhenValidated_ThenTheSelectedLengthLimitIsApplied(bool useLongResourceIds, int length, bool expectedValid)
        {
            // Arrange
            var observation = Samples.GetDefaultObservation().UpdateId(new string('a', length));

            // Act
            bool isValid = GetValidationFailures(observation, ResourceIdPolicy.From(useLongResourceIds));

            // Assert
            Assert.Equal(expectedValid, isValid);
        }

        [Theory]
        [InlineData("a_b")]
        [InlineData("a/b")]
        public void GivenAnInvalidLongId_WhenProcessingAResource_ThenValidationFails(string id)
        {
            // Arrange
            var observation = Samples.GetDefaultObservation().UpdateId(id);

            // Act
            bool isValid = GetValidationFailures(observation, ResourceIdPolicy.Extended);

            // Assert
            Assert.False(isValid);
        }

        private static bool GetValidationFailures(ResourceElement defaultObservation, ResourceIdPolicy policy)
        {
            var validator = new IdValidator<ResourceElement>(policy);
            var validationContext = new ValidationContext<ResourceElement>(defaultObservation);
            return validator.IsValid(validationContext, defaultObservation.Id);
        }
    }
}
