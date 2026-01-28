// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Extensions
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Schema)]
    public class BaseExtensionsTests
    {
        [Fact]
        public void EqualValues_BothReferencesAreSame_ReturnsTrue()
        {
            // Arrange
            var element = new FhirString("value");

            // Act
            var result = element.EqualValues(element);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void EqualValues_BothHaveNoChildrenAndSameValue_ReturnsTrue()
        {
            // Arrange
            var element1 = new FhirString("value");
            var element2 = new FhirString("value");

            // Act
            var result = element1.EqualValues(element2);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void EqualValues_DifferentTypeNames_ReturnsFalse()
        {
            // Arrange
            var element1 = new FhirString("value");
            var element2 = new FhirBoolean(true);

            // Act
            var result = element1.EqualValues(element2);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void EqualValues_DifferentValues_ReturnsFalse()
        {
            // Arrange
            var element1 = new FhirString("value1");
            var element2 = new FhirString("value2");

            // Act
            var result = element1.EqualValues(element2);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void EqualValues_ComplexTypesSameValues_ReturnsTrue()
        {
            // Arrange
            var value1 = new Quantity { Value = 10, Unit = "mg" };
            var value2 = new Quantity { Value = 10, Unit = "mg" };

            // Act
            var result = value1.EqualValues(value2);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void EqualValues_ComplexTypesDifferentValues_ReturnsFalse()
        {
            // Arrange
            var value1 = new Quantity { Value = 10, Unit = "mg" };
            var value2 = new Quantity { Value = 20, Unit = "mg" };

            // Act
            var result = value1.EqualValues(value2);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void EqualValues_WithExplicitNames_SameNames_ReturnsTrue()
        {
            // Arrange
            var element1 = new FhirString("value");
            var element2 = new FhirString("value");

            // Act
            var result = element1.EqualValues("testName", element2, "testName");

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void EqualValues_WithExplicitNames_DifferentNames_ReturnsFalse()
        {
            // Arrange
            var element1 = new FhirString("value");
            var element2 = new FhirString("value");

            // Act
            var result = element1.EqualValues("name1", element2, "name2");

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void EqualValues_NullValues_ReturnsTrue()
        {
            // Arrange
            Base element1 = null;
            Base element2 = null;

            // Act
            var result = element1.EqualValues(element2);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void EqualValues_OneNull_ReturnsFalse()
        {
            // Arrange
            Base element1 = new FhirString("value");
            Base element2 = null;

            // Act
            var result = element1.EqualValues(element2);

            // Assert
            Assert.False(result);
        }
    }
}
