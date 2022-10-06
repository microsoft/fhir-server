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
    }
}
