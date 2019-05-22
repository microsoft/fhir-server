// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using FluentValidation;
using FluentValidation.Internal;
using FluentValidation.Results;
using FluentValidation.Validators;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Validation.FhirPrimitiveTypes;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Validation.FhirPrimitiveTypes
{
    public class IdValidatorTests
    {
        [Theory]
        [InlineData("1+1")]
        [InlineData("1_1")]
        [InlineData("11|")]
        [InlineData("00000000000000000000000000000000000000000000000000000000000000065")]
        public void GivenAnInvalidId_WhenProcessingAResource_ThenAValidationMessageIsCreated(string id)
        {
            var defaultObservation = Samples.GetDefaultObservation()
                .UpdateId(id);

            IEnumerable<ValidationFailure> result = GetValidationFailures(defaultObservation);

            Assert.Single(result);
        }

        [Theory]
        [InlineData("1.1")]
        [InlineData("id1")]
        [InlineData("example")]
        [InlineData("a94060e6-038e-411b-a64b-38c2c3ff0fb7")]
        [InlineData("AF30C45C-94AC-4DE3-89D8-9A20BB2A973F")]
        [InlineData("0000000000000000000000000000000000000000000000000000000000000064")]
        public void GivenAValidId_WhenProcessingAResource_ThenAValidationMessageIsCreated(string id)
        {
            var defaultObservation = Samples.GetDefaultObservation().UpdateId(id);

            IEnumerable<ValidationFailure> result = GetValidationFailures(defaultObservation);

            Assert.Empty(result);
        }

        private static IEnumerable<ValidationFailure> GetValidationFailures(ResourceElement defaultObservation)
        {
            var validator = new IdValidator();

            var result = validator.Validate(
                new PropertyValidatorContext(new ValidationContext(defaultObservation), PropertyRule.Create<ResourceElement, string>(x => x.Id), "Id"));

            return result;
        }
    }
}
