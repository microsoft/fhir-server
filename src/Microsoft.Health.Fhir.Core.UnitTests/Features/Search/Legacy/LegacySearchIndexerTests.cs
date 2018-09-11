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
using Microsoft.Health.Fhir.Core.Features.Search.Legacy.SearchValues;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search.Legacy
{
    public class LegacySearchIndexerTests
    {
        private const string ParamNameResourceTypeManifestManager = "resourceTypeManifestManager";
        private const string ParamNameResource = "resource";

        private readonly IResourceTypeManifestManager _resourceTypeManifestManager = Substitute.For<IResourceTypeManifestManager>();
        private readonly LegacySearchIndexer _searchIndexer;

        public LegacySearchIndexerTests()
        {
            _searchIndexer = new LegacySearchIndexer(_resourceTypeManifestManager);
        }

        [Fact]
        public void GivenANullResourceTypeManifestManager_WhenInitializing_ThenExceptionShouldBeThrown()
        {
            Assert.Throws<ArgumentNullException>(
                ParamNameResourceTypeManifestManager,
                () => new LegacySearchIndexer(null));
        }

        [Fact]
        public void GivenANullResource_WhenExtracting_ThenExceptionShouldBeThrown()
        {
            Assert.Throws<ArgumentNullException>(ParamNameResource, () => _searchIndexer.Extract(null));
        }

        [Fact]
        public void GivenAResourceThatIsNotSupported_WhenExtracting_ThenEmptyResultShouldBeReturned()
        {
            Patient patient = new Patient();

            _resourceTypeManifestManager.GetManifest(patient.GetType()).Returns(
                x => throw new ResourceNotSupportedException(patient.GetType()));

            IReadOnlyCollection<SearchIndexEntry> entries = _searchIndexer.Extract(patient);

            Assert.NotNull(entries);
            Assert.Empty(entries);
        }

        [Fact]
        public void GivenExtractingFromSearchParamReturnsNull_WhenExtracting_ThenEmptyResultShouldBeReturned()
        {
            Patient patient = new Patient();

            string paramName = "param";

            SetupResourceTypeManifestManager(
                patient,
                CreateSearchParam(patient, paramName, null));

            IReadOnlyCollection<SearchIndexEntry> entries = _searchIndexer.Extract(patient);

            Assert.NotNull(entries);
            Assert.Empty(entries);
        }

        [Fact]
        public void GivenExtractingFromSearchParamReturnsOne_WhenExtracting_ThenResultShouldBeReturned()
        {
            Patient patient = new Patient();

            string paramName = "param";

            ISearchValue searchValue = Substitute.For<ISearchValue>();

            SetupResourceTypeManifestManager(
                patient,
                CreateSearchParam(patient, paramName, searchValue));

            IReadOnlyCollection<SearchIndexEntry> entries = _searchIndexer.Extract(patient);

            Assert.NotNull(entries);
            Assert.Single(entries);
            Assert.Collection(
                entries,
                entry => ValidateSearchIndexEntry(entry, paramName, searchValue));
        }

        [Fact]
        public void GivenExtractingFromSearchParamReturnsMultiple_WhenExtracting_ThenResultShouldBeReturned()
        {
            Patient patient = new Patient();

            string paramName = "param";

            ISearchValue searchValue1 = Substitute.For<ISearchValue>();
            ISearchValue searchValue2 = Substitute.For<ISearchValue>();

            SetupResourceTypeManifestManager(
                patient,
                CreateSearchParam(patient, paramName, searchValue1, searchValue2));

            IReadOnlyCollection<SearchIndexEntry> entries = _searchIndexer.Extract(patient);

            Assert.NotNull(entries);
            Assert.Equal(2, entries.Count);
            Assert.Collection(
                entries,
                entry => ValidateSearchIndexEntry(entry, paramName, searchValue1),
                entry => ValidateSearchIndexEntry(entry, paramName, searchValue2));
        }

        [Fact]
        public void GivenResourceSupportsMultipleSearchParams_WhenExtracting_ThenResultShouldBeReturned()
        {
            Organization organization = new Organization();

            string paramName1 = "param";
            string paramName2 = "test";

            ISearchValue searchValue1 = Substitute.For<ISearchValue>();
            ISearchValue searchValue2 = Substitute.For<ISearchValue>();
            ISearchValue searchValue3 = Substitute.For<ISearchValue>();

            SetupResourceTypeManifestManager(
                organization,
                CreateSearchParam(organization, paramName1, searchValue1, searchValue2),
                CreateSearchParam(organization, paramName2, searchValue3));

            IReadOnlyCollection<SearchIndexEntry> entries = _searchIndexer.Extract(organization);

            Assert.NotNull(entries);
            Assert.Equal(3, entries.Count);
            Assert.Collection(
                entries,
                entry => ValidateSearchIndexEntry(entry, paramName1, searchValue1),
                entry => ValidateSearchIndexEntry(entry, paramName1, searchValue2),
                entry => ValidateSearchIndexEntry(entry, paramName2, searchValue3));
        }

        private SearchParam CreateSearchParam(Resource resource, string paramName, params ISearchValue[] searchValues)
        {
            SearchParam searchParam = new SearchParam(resource.GetType(), paramName, SearchParamType.String, s => null);

            ISearchValuesExtractor extractor = Substitute.For<ISearchValuesExtractor>();

            extractor.Extract(resource).Returns(searchValues);

            searchParam.AddExtractor(extractor);

            return searchParam;
        }

        private void SetupResourceTypeManifestManager(Resource resource, params SearchParam[] searchParams)
        {
            ResourceTypeManifest resourceTypeManifest = new ResourceTypeManifest(
                resource.GetType(),
                searchParams);

            _resourceTypeManifestManager.GetManifest(resource.GetType()).Returns(resourceTypeManifest);
        }

        private void ValidateSearchIndexEntry(SearchIndexEntry actual, string paramName, ISearchValue value)
        {
            Assert.NotNull(actual);
            Assert.Equal(paramName, actual.ParamName);
            Assert.Same(value, actual.Value);
        }
    }
}
