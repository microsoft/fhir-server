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

            var result = GetValidationFailures(defaultObservation);

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

            var result = GetValidationFailures(defaultObservation);

            Assert.True(result);
        }

        private static bool GetValidationFailures(ResourceElement defaultObservation)
        {
            var validator = new IdValidator<ResourceElement>();
            var validationContext = new ValidationContext<ResourceElement>(defaultObservation);
            return validator.IsValid(validationContext, defaultObservation.Id);
        }
    }
}
