// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Legacy;
using Microsoft.Health.Fhir.Core.Features.Search.Legacy.SearchValues;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search.Legacy.Manifests
{
    public class ResourceTypeManifestTests
    {
        private const string ParamNameResourceType = "resourceType";
        private const string ParamNameSupportedSearchParams = "supportedSearchParams";
        private const string ParamNameParamName = "paramName";

        private static readonly Type DefaultResourceType = typeof(Observation);

        private static readonly SearchParam DefaultSearchParam = new SearchParam(
            DefaultResourceType,
            "search",
            SearchParamType.Number,
            NumberSearchValue.Parse);

        private static readonly IReadOnlyCollection<SearchParam> DefaultSupportedParams = new[]
        {
            DefaultSearchParam,
        };

        private readonly ResourceManifestBuilder _builder = new ResourceManifestBuilder();

        [Fact]
        public void GivenANullResourceType_WhenInitializing_ThenExceptionShouldBeThrown()
        {
            _builder.ResourceType = null;

            Assert.Throws<ArgumentNullException>(ParamNameResourceType, () => _builder.ToResourceManifest());
        }

        [Fact]
        public void GivenAnIncorrectResourceType_WhenInitializing_ThenExceptionShouldBeThrown()
        {
            _builder.ResourceType = typeof(int);

            Assert.Throws<ArgumentException>(ParamNameResourceType, () => _builder.ToResourceManifest());
        }

        [Fact]
        public void GivenANullSupportedSearchParams_WhenInitializing_ThenExceptionShouldBeThrown()
        {
            _builder.SupportedParams = null;

            Assert.Throws<ArgumentNullException>(ParamNameSupportedSearchParams, () => _builder.ToResourceManifest());
        }

        [Fact]
        public void GivenAnEmptySupportedSearchParams_WhenInitializing_ThenExceptionShouldBeThrown()
        {
            _builder.SupportedParams = new SearchParam[0];

            Assert.Throws<ArgumentException>(ParamNameSupportedSearchParams, () => _builder.ToResourceManifest());
        }

        [Fact]
        public void GivenAResourceType_WhenInitialized_ThenResourceTypeShouldBeAssigned()
        {
            ResourceTypeManifest manifest = _builder.ToResourceManifest();

            Assert.Equal(DefaultResourceType, manifest.ResourceType);
        }

        [Fact]
        public void GivenSupportedSearchParams_WhenInitialized_ThenSupportedSearchParamsShouldBeAssigned()
        {
            ResourceTypeManifest manifest = _builder.ToResourceManifest();

            Assert.Equal(DefaultSupportedParams, manifest.SupportedSearchParams);
        }

        [Fact]
        public void GivenSupportedSearchParams_WhenInitialized_ThenSupportedSearchParamsShouldBeSortedByParamName()
        {
            SearchParam[] input = new[]
            {
                new SearchParam(DefaultResourceType, "xyz", SearchParamType.Number, NumberSearchValue.Parse),
                new SearchParam(DefaultResourceType, "abc", SearchParamType.String, StringSearchValue.Parse),
            };

            _builder.SupportedParams = input;

            ResourceTypeManifest manifest = _builder.ToResourceManifest();

            IEnumerable<SearchParam> expected = new[]
            {
                input[1],
                input[0],
            };

            Assert.Equal(expected, manifest.SupportedSearchParams);
        }

        [Fact]
        public void GivenANullParamName_WhenGettingSearchParam_ThenExceptionShouldBeThrown()
        {
            ResourceTypeManifest manifest = _builder.ToResourceManifest();

            Assert.Throws<ArgumentNullException>(ParamNameParamName, () => manifest.GetSearchParam(null));
        }

        [Theory]
        [InlineData("")]
        [InlineData("    ")]
        public void GivenAnInvalidSearchParam_WhenGettingSearchParam_ThenExceptionShouldBeThrown(string s)
        {
            ResourceTypeManifest manifest = _builder.ToResourceManifest();

            Assert.Throws<ArgumentException>(ParamNameParamName, () => manifest.GetSearchParam(s));
        }

        [Fact]
        public void GivenASupportedSearchParamName_WhenGettingSearchParam_ThenCorrespondingSearchParamShouldBeReturned()
        {
            ResourceTypeManifest manifest = _builder.ToResourceManifest();

            SearchParam searchParam = manifest.GetSearchParam(DefaultSearchParam.ParamName);

            Assert.NotNull(searchParam);
            Assert.Equal(DefaultSearchParam, searchParam);
        }

        [Fact]
        public void GivenANotSupportedSearchParamName_WhenGettingSearchParam_ThenExceptionShouldBeThrown()
        {
            ResourceTypeManifest manifest = _builder.ToResourceManifest();

            Assert.Throws<SearchParameterNotSupportedException>(() => manifest.GetSearchParam("abc"));
        }

        private class ResourceManifestBuilder
        {
            public ResourceManifestBuilder()
            {
                ResourceType = DefaultResourceType;
                SupportedParams = DefaultSupportedParams;
            }

            public Type ResourceType { get; set; }

            public IReadOnlyCollection<SearchParam> SupportedParams { get; set; }

            public ResourceTypeManifest ToResourceManifest()
            {
                return new ResourceTypeManifest(ResourceType, SupportedParams);
            }
        }
    }
}
