// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search.SearchValues
{
    [Trait("Traits.OwningTeam", OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class ReferenceSearchValueTests
    {
        private const string ParamNameResourceType = "resourceType";
        private const string ParamNameResourceId = "resourceId";

        private static readonly ReferenceKind DefaultReferenceKind = ReferenceKind.InternalOrExternal;
        private static readonly Uri DefaultBaseUri = new Uri("http://localhost");
        private static readonly ResourceType DefaultResourceType = ResourceType.Location;
        private static readonly string DefaultResourceId = "123";

        private readonly ReferenceSearchValueBuilder _builder = new ReferenceSearchValueBuilder();

        [Fact]
        public void GivenANonNullBaseUriAndNullResourceType_WhenInitializing_ThenArgumentNullExceptionShouldBeThrown()
        {
            _builder.ResourceType = null;

            Assert.Throws<ArgumentException>(ParamNameResourceType, () => _builder.ToReferenceSearchValue());
        }

        [Fact]
        public void GivenANullResourceId_WhenInitializing_ThenArgumentNullExceptionShouldBeThrown()
        {
            _builder.ResourceId = null;

            Assert.Throws<ArgumentNullException>(ParamNameResourceId, () => _builder.ToReferenceSearchValue());
        }

        [Theory]
        [InlineData("")]
        [InlineData("    ")]
        public void GivenAnInvalidResourceId_WhenInitializing_ThenArgumentExceptionShouldBeThrown(string resourceId)
        {
            _builder.ResourceId = resourceId;

            Assert.Throws<ArgumentException>(ParamNameResourceId, () => _builder.ToReferenceSearchValue());
        }

        [Fact]
        public void GivenASearchValue_WhenIsValidCompositeComponentIsCalled_ThenTrueShouldBeReturned()
        {
            var value = _builder.ToReferenceSearchValue();

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
            Uri uri = null;

            if (uriString != null)
            {
                uri = new Uri(uriString);
            }

            var value = new ReferenceSearchValue(ReferenceKind.InternalOrExternal, uri, resourceType.ToString(), resourceId);

            Assert.Equal(expected, value.ToString());
        }

        private class ReferenceSearchValueBuilder
        {
            public ReferenceSearchValueBuilder()
            {
                Kind = DefaultReferenceKind;
                BaseUri = DefaultBaseUri;
                ResourceType = DefaultResourceType;
                ResourceId = DefaultResourceId;
            }

            public ReferenceKind Kind { get; set; }

            public Uri BaseUri { get; set; }

            public ResourceType? ResourceType { get; set; }

            public string ResourceId { get; set; }

            public ReferenceSearchValue ToReferenceSearchValue()
            {
                return new ReferenceSearchValue(Kind, BaseUri, ResourceType.ToString(), ResourceId);
            }
        }
    }
}
