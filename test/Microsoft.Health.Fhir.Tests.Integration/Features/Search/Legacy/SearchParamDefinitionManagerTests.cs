// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Legacy;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.Integration.Features.Search.Legacy
{
    public class SearchParamDefinitionManagerTests
    {
        private const string ParamNameResourceType = "resourceType";
        private const string ParamNameParamName = "paramName";

        private static readonly Type DefaultResourceType = typeof(Organization);
        private static readonly string DefaultParamName = "name";

        private readonly SearchParamDefinitionManager _manager = new SearchParamDefinitionManager();

        [Fact]
        public void GivenANullResourceType_WhenGettingSearchParamType_ThenExceptionShouldBeThrown()
        {
            Assert.Throws<ArgumentNullException>(ParamNameResourceType, () => _manager.GetSearchParamType(null, DefaultParamName));
        }

        [Fact]
        public void GivenAnInvalidResourceType_WhenGettingSearchParamType_ThenExceptionShouldBeThrown()
        {
            Assert.Throws<ArgumentException>(ParamNameResourceType, () => _manager.GetSearchParamType(typeof(int), DefaultParamName));
        }

        [Fact]
        public void GivenANullParamName_WhenGettingSearchParamType_ThenExceptionShouldBeThrown()
        {
            Assert.Throws<ArgumentNullException>(ParamNameParamName, () => _manager.GetSearchParamType(DefaultResourceType, null));
        }

        [Theory]
        [InlineData("")]
        [InlineData("    ")]
        public void GivenAnInvalidParamName_WhenGettingSearchParamType_ThenExceptionShouldBeThrown(string s)
        {
            Assert.Throws<ArgumentException>(ParamNameParamName, () => _manager.GetSearchParamType(DefaultResourceType, s));
        }

        [Fact]
        public void GivenANotSupportedResourceType_WhenGettingSearchParamType_ThenExceptionShouldBeThrown()
        {
            Assert.Throws<ResourceNotSupportedException>(() => _manager.GetSearchParamType(typeof(TestResource), DefaultParamName));
        }

        [Fact]
        public void GivenANotSupportedSearchParam_WhenGettingSearchParamType_ThenExceptionShouldBeThrown()
        {
            Assert.Throws<SearchParameterNotSupportedException>(() => _manager.GetSearchParamType(DefaultResourceType, "abc"));
        }

        [Fact]
        public void GivenASupportedResourceTypeAndSearchParam_WhenGettingSearchParamType_ThenCorrectSearchParamTypeShouldBeReturned()
        {
            SearchParamType type = _manager.GetSearchParamType(DefaultResourceType, DefaultParamName);

            Assert.Equal(SearchParamType.String, type);
        }

        [Fact]
        public void GivenANullResourceType_WhenGettingReferenceTargetResourceTypes_ThenExceptionShouldBeThrown()
        {
            Assert.Throws<ArgumentNullException>(ParamNameResourceType, () => _manager.GetReferenceTargetResourceTypes(null, DefaultParamName));
        }

        [Fact]
        public void GivenAnInvalidResourceType_WhenGettingReferenceTargetResourceTypes_ThenExceptionShouldBeThrown()
        {
            Assert.Throws<ArgumentException>(ParamNameResourceType, () => _manager.GetReferenceTargetResourceTypes(typeof(int), DefaultParamName));
        }

        [Fact]
        public void GivenANullParamName_WhenGettingReferenceTargetResourceTypes_ThenExceptionShouldBeThrown()
        {
            Assert.Throws<ArgumentNullException>(ParamNameParamName, () => _manager.GetReferenceTargetResourceTypes(DefaultResourceType, null));
        }

        [Theory]
        [InlineData("")]
        [InlineData("    ")]
        public void GivenAnInvalidParamName_WhenGettingReferenceTargetResourceTypes_ThenExceptionShouldBeThrown(string s)
        {
            Assert.Throws<ArgumentException>(ParamNameParamName, () => _manager.GetReferenceTargetResourceTypes(DefaultResourceType, s));
        }

        [Fact]
        public void GivenANotSupportedResourceType_WhenGettingReferenceTargetResourceTypes_ThenExceptionShouldBeThrown()
        {
            Assert.Throws<ResourceNotSupportedException>(() => _manager.GetReferenceTargetResourceTypes(typeof(TestResource), DefaultParamName));
        }

        [Fact]
        public void GivenANotSupportedSearchParam_WhenGettingReferenceTargetResourceTypes_ThenExceptionShouldBeThrown()
        {
            Assert.Throws<SearchParameterNotSupportedException>(() => _manager.GetReferenceTargetResourceTypes(DefaultResourceType, "abc"));
        }

        [Fact]
        public void GivenANonReferenceTypeSearchParam_WhenGettingReferenceTargetResourceTypes_ThenExceptionShouldBeThrown()
        {
            Assert.Throws<InvalidOperationException>(() => _manager.GetReferenceTargetResourceTypes(DefaultResourceType, DefaultParamName));
        }

        [Fact]
        public void GivenAValidReferenceTypeSearchParam_WhenGettingReferenceTargetResourceTypes_ThenCorrectReferenceTargetResourceTypesShouldBeReturned()
        {
            IEnumerable<Type> targetResourceTypes = _manager.GetReferenceTargetResourceTypes(typeof(Patient), "general-practitioner");

            IEnumerable<Type> expected = new[] { typeof(Organization), typeof(Practitioner) };

            Assert.NotNull(targetResourceTypes);
            Assert.Equal(expected, targetResourceTypes);
        }

        private class TestResource : Resource
        {
            public override IDeepCopyable DeepCopy()
            {
                return null;
            }
        }
    }
}
