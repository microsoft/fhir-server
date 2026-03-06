// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Search.FhirPath;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search.FhirPath
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class FirelyFhirPathProviderTests
    {
        private readonly IFhirPathProvider _provider;

        public FirelyFhirPathProviderTests()
        {
            _provider = new FirelyFhirPathProvider();
        }

        [Fact]
        public void GivenASimpleExpression_WhenCompiled_ThenReturnsCompiledExpression()
        {
            // Arrange
            const string expression = "Patient.name";

            // Act
            var compiled = _provider.Compile(expression);

            // Assert
            Assert.NotNull(compiled);
            Assert.Equal(expression, compiled.Expression);
        }

        [Fact]
        public void GivenTheSameExpression_WhenCompiledMultipleTimes_ThenReturnsCachedInstance()
        {
            // Arrange
            const string expression = "Patient.name";

            // Act
            var first = _provider.Compile(expression);
            var second = _provider.Compile(expression);

            // Assert
            Assert.Same(first, second);
        }

        [Fact]
        public void GivenAPatientResource_WhenEvaluatingNameExpression_ThenReturnsNameElements()
        {
            // Arrange
            var patient = new Patient
            {
                Name =
                {
                    new HumanName { Family = "Smith", Given = new[] { "John" } },
                },
            };
            var element = patient.ToTypedElement();

            // Act
            var results = _provider.Evaluate(element, "Patient.name").ToList();

            // Assert
            Assert.Single(results);
            Assert.Equal("HumanName", results[0].InstanceType);
        }

        [Fact]
        public void GivenAPatientResource_WhenEvaluatingFamilyExpression_ThenReturnsStringValue()
        {
            // Arrange
            var patient = new Patient
            {
                Name =
                {
                    new HumanName { Family = "Smith" },
                },
            };
            var element = patient.ToTypedElement();

            // Act
            var result = _provider.Scalar<string>(element, "Patient.name.family");

            // Assert
            Assert.Equal("Smith", result);
        }

        [Fact]
        public void GivenAPatientResource_WhenEvaluatingActiveExpression_ThenReturnsBoolValue()
        {
            // Arrange
            var patient = new Patient { Active = true };
            var element = patient.ToTypedElement();

            // Act
            var result = _provider.Predicate(element, "Patient.active");

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void GivenAnInactivePatient_WhenEvaluatingActiveExpression_ThenReturnsFalse()
        {
            // Arrange
            var patient = new Patient { Active = false };
            var element = patient.ToTypedElement();

            // Act
            var result = _provider.Predicate(element, "Patient.active");

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void GivenAPatientWithNoName_WhenEvaluatingNameExists_ThenReturnsFalse()
        {
            // Arrange
            var patient = new Patient();
            var element = patient.ToTypedElement();

            // Act
            var result = _provider.Predicate(element, "Patient.name.exists()");

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void GivenAPatientWithName_WhenEvaluatingNameExists_ThenReturnsTrue()
        {
            // Arrange
            var patient = new Patient
            {
                Name = { new HumanName { Family = "Smith" } },
            };
            var element = patient.ToTypedElement();

            // Act
            var result = _provider.Predicate(element, "Patient.name.exists()");

            // Assert
            Assert.True(result);
        }
    }
}
