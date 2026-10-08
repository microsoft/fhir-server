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
        [InlineData("1+1", false)]
        [InlineData("1+1", true)]
        [InlineData("1_1", false)]
        [InlineData("1_1", true)]
        [InlineData("11|", false)]
        [InlineData("11|", true)]
        [InlineData("a/b", false)]
        [InlineData("a/b", true)]
        [InlineData("00000000000000000000000000000000000000000000000000000000000000000", false)] // 65 chars
        [InlineData("00000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000", false)] // 128 chars
        [InlineData("000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000", true)] // 129 chars
        public void GivenAnInvalidId_WhenProcessingAResource_ThenAValidationMessageWithAFhirPathIsCreated(string id, bool useLongResourceIds)
        {
            var defaultObservation = Samples.GetDefaultObservation().UpdateId(id);

            var result = GetValidationFailures(defaultObservation, ResourceIdPolicy.From(useLongResourceIds));

            Assert.False(result);
        }

        [Theory]
        [InlineData("1.1", false)]
        [InlineData("1.1", true)]
        [InlineData("id1", false)]
        [InlineData("example", false)]
        [InlineData("a94060e6-038e-411b-a64b-38c2c3ff0fb7", false)]
        [InlineData("AF30C45C-94AC-4DE3-89D8-9A20BB2A973F", false)]
        [InlineData("0000000000000000000000000000000000000000000000000000000000000000", false)] // 64 chars
        [InlineData("0000000000000000000000000000000000000000000000000000000000000000", true)] // 64 chars
        [InlineData("00000000000000000000000000000000000000000000000000000000000000000", true)] // 65 chars
        [InlineData("00000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000", true)] // 128 chars
        public void GivenAValidId_WhenProcessingAResource_ThenAValidationMessageIsNotCreated(string id, bool useLongResourceIds)
        {
            var defaultObservation = Samples.GetDefaultObservation().UpdateId(id);

            var result = GetValidationFailures(defaultObservation, ResourceIdPolicy.From(useLongResourceIds));

            Assert.True(result);
        }

        private static bool GetValidationFailures(ResourceElement defaultObservation, ResourceIdPolicy policy)
        {
            var validator = new IdValidator<ResourceElement>(policy);
            var validationContext = new ValidationContext<ResourceElement>(defaultObservation);
            return validator.IsValid(validationContext, defaultObservation.Id);
        }
    }
}
