// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Hl7.Fhir.Support;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Extensions
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Schema)]
    public class ElementValueExtensionsTests
    {
        [Fact]
        public void EqualValues_BothReferencesAreSame_ReturnsTrue()
        {
            // Arrange
            var element = new ElementValue("test", new FhirString("value"));

            // Act
            var result = element.EqualValues(element);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void EqualValues_BothHaveNoChildrenAndSameNameAndValue_ReturnsTrue()
        {
            // Arrange
            var element1 = new ElementValue("name", new FhirString("value"));
            var element2 = new ElementValue("name", new FhirString("value"));

            // Act
            var result = element1.EqualValues(element2);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void EqualValues_DifferentElementNames_ReturnsFalse()
        {
            // Arrange
            var element1 = new ElementValue("name1", new FhirString("value"));
            var element2 = new ElementValue("name2", new FhirString("value"));

            // Act
            var result = element1.EqualValues(element2);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void EqualValues_DifferentValues_ReturnsFalse()
        {
            // Arrange
            var element1 = new ElementValue("name", new FhirString("value1"));
            var element2 = new ElementValue("name", new FhirString("value2"));

            // Act
            var result = element1.EqualValues(element2);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void EqualValues_OneHasChildrenOtherDoesNot_ReturnsFalse()
        {
            // Arrange
            var child = new ElementValue("child", new FhirString("cval"));
            var valueWithChild = Substitute.For<Base>();
            valueWithChild.NamedChildren.Returns(new List<ElementValue> { child });

            var valueWithoutChild = Substitute.For<Base>();
            valueWithoutChild.NamedChildren.Returns(Enumerable.Empty<ElementValue>());

            var element1 = new ElementValue("name", valueWithChild);
            var element2 = new ElementValue("name", valueWithoutChild);

            // Act
            var result = element1.EqualValues(element2);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void EqualValues_BothHaveChildrenWithDifferentCounts_ReturnsFalse()
        {
            // Arrange
            var child1 = new ElementValue("child", new FhirString("cval1"));
            var child2 = new ElementValue("child", new FhirString("cval2"));

            var value1 = Substitute.For<Base>();
            value1.NamedChildren.Returns(new List<ElementValue> { child1 });

            var value2 = Substitute.For<Base>();
            value2.NamedChildren.Returns(new List<ElementValue> { child1, child2 });

            var element1 = new ElementValue("name", value1);
            var element2 = new ElementValue("name", value2);

            // Act
            var result = element1.EqualValues(element2);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void EqualValues_BothHaveChildrenWithSameValues_ReturnsTrue()
        {
            // Arrange
            var value1 = new OperationOutcome();
            value1.AddIssue("test", Issue.Create(1, OperationOutcome.IssueSeverity.Error, OperationOutcome.IssueType.Structure));

            var value2 = new OperationOutcome();
            value2.AddIssue("test", Issue.Create(1, OperationOutcome.IssueSeverity.Error, OperationOutcome.IssueType.Structure));

            var element1 = new ElementValue("name", value1);
            var element2 = new ElementValue("name", value2);

            // Act
            var result = element1.EqualValues(element2);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void EqualValues_BothHaveChildrenWithDifferentValues_ReturnsFalse()
        {
            // Arrange
            var value1 = new OperationOutcome();
            value1.AddIssue("test", Issue.Create(1, OperationOutcome.IssueSeverity.Error, OperationOutcome.IssueType.Structure));

            var value2 = new OperationOutcome();
            value2.AddIssue("test", Issue.Create(1, OperationOutcome.IssueSeverity.Warning, OperationOutcome.IssueType.Structure));

            var element1 = new ElementValue("name", value1);
            var element2 = new ElementValue("name", value2);

            // Act
            var result = element1.EqualValues(element2);

            // Assert
            Assert.False(result);
        }
    }
}
