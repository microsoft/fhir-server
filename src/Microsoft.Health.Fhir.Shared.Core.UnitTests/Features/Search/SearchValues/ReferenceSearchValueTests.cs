// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Features.Validation.FhirPrimitiveTypes;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search.SearchValues
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class ReferenceSearchValueTests
    {
        private const string ParamNameResourceType = "resourceType";
        private const string ParamNameId = "id";
        private const string ParamNameExactValue = "exactValue";

        private static readonly Uri DefaultBaseUri = new Uri("http://localhost");

        [Fact]
        public void GivenANonNullBaseUriAndNullResourceType_WhenInitializing_ThenArgumentNullExceptionShouldBeThrown()
        {
            Assert.Throws<ArgumentNullException>(ParamNameResourceType, () => ReferenceSearchValue.FromResource(ReferenceKind.External, DefaultBaseUri, null, CreateId("123")));
        }

        [Fact]
        public void GivenANullResourceId_WhenInitializing_ThenArgumentNullExceptionShouldBeThrown()
        {
            Assert.Throws<ArgumentNullException>(ParamNameId, () => ReferenceSearchValue.FromResource(ReferenceKind.InternalOrExternal, null, ResourceType.Location.ToString(), null));
        }

        [Theory]
        [InlineData("")]
        [InlineData("    ")]
        public void GivenAnInvalidLiteral_WhenInitializing_ThenArgumentExceptionShouldBeThrown(string literal)
        {
            Assert.Throws<ArgumentException>(ParamNameExactValue, () => ReferenceSearchValue.FromLiteral(literal));
        }

        [Fact]
        public void GivenANullLiteral_WhenInitializing_ThenArgumentNullExceptionShouldBeThrown()
        {
            Assert.Throws<ArgumentNullException>(ParamNameExactValue, () => ReferenceSearchValue.FromLiteral(null));
        }

        [Fact]
        public void GivenASearchValue_WhenIsValidCompositeComponentIsCalled_ThenTrueShouldBeReturned()
        {
            var value = ReferenceSearchValue.FromResource(ReferenceKind.InternalOrExternal, DefaultBaseUri, ResourceType.Location.ToString(), CreateId("123"));

            Assert.True(value.IsValidAsCompositeComponent);
        }

        [Theory]
        [InlineData("http://localhost/", ResourceType.Patient, "123", "http://localhost/Patient/123")]
        [InlineData(null, ResourceType.Observation, "xyz", "Observation/xyz")]
        [InlineData(null, null, "abc", "abc")]
        public void GivenASearchValue_WhenToStringIsCalled_ThenCorrectStringShouldBeReturned(
            string uriString,
            ResourceType? resourceType,
            string resourceId,
            string expected)
        {
            Uri uri = uriString == null ? null : new Uri(uriString);

            var value = ReferenceSearchValue.FromResource(ReferenceKind.InternalOrExternal, uri, resourceType?.ToString(), CreateId(resourceId));

            Assert.Equal(expected, value.ToString());
        }

        [Fact]
        public void GivenALiteral_WhenCreated_ThenTheCompleteValueIsPreserved()
        {
            string literal = "urn:uuid:" + new string('a', 200);

            var value = ReferenceSearchValue.FromLiteral(literal);

            Assert.Equal(literal, value.ResourceId);
            Assert.Equal(ReferenceKind.InternalOrExternal, value.Kind);
            Assert.Null(value.BaseUri);
            Assert.Null(value.ResourceType);
        }

        [Fact]
        public void GivenALiteral_WhenTheMatchingContextChanges_ThenTheCompleteLiteralIsPreserved()
        {
            string literal = "urn:uuid:" + new string('a', 200);

            var value = ReferenceSearchValue.FromLiteral(literal).WithMatchingContext(ReferenceKind.Internal, null, ResourceType.Patient.ToString());

            Assert.Equal(literal, value.ResourceId);
            Assert.Equal(ReferenceKind.Internal, value.Kind);
            Assert.Equal(ResourceType.Patient.ToString(), value.ResourceType);
        }

        [Fact]
        public void GivenABoundedId_WhenTheMatchingContextChanges_ThenTheCompleteIdIsPreserved()
        {
            string id = new string('a', ResourceIdPolicy.LongMaxLength);

            var value = ReferenceSearchValue.FromResource(ReferenceKind.InternalOrExternal, null, null, BoundedResourceId.Parse(id, ResourceIdPolicy.Extended))
                .WithMatchingContext(ReferenceKind.External, DefaultBaseUri, ResourceType.Patient.ToString());

            Assert.Equal(id, value.ResourceId);
            Assert.Equal(ReferenceKind.External, value.Kind);
            Assert.Equal(DefaultBaseUri, value.BaseUri);
        }

        [Fact]
        public void GivenALiteralAndABoundedIdWithTheSameText_WhenCompared_ThenTheyAreEqual()
        {
            var literal = ReferenceSearchValue.FromLiteral("abc");
            var id = ReferenceSearchValue.FromResource(ReferenceKind.InternalOrExternal, null, null, CreateId("abc"));

            Assert.True(literal.Equals(id));
            Assert.Equal(literal.GetHashCode(), id.GetHashCode());
        }

        private static BoundedResourceId CreateId(string value) => BoundedResourceId.Parse(value, ResourceIdPolicy.Standard);
    }
}
