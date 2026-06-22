// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Microsoft.Health.Extensions.Xunit;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Messages.Upsert;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Test.Utilities;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    [FhirStorageTestsFixtureArgumentSets(DataStore.SqlServer)]
    public class SearchParameterCachePopulationTests : IClassFixture<FhirStorageTestsFixture>
    {
        private readonly FhirStorageTestsFixture _fixture;
        private readonly IFhirStorageTestHelper _testHelper;

        public SearchParameterCachePopulationTests(FhirStorageTestsFixture fixture)
        {
            _fixture = fixture;
            _testHelper = fixture.TestHelper;
        }

        private async Task<string> CreateSearchParameterResourceAsync(string url, CancellationToken cancellationToken)
        {
#if R5
            var resourceTypes = new List<VersionIndependentResourceTypesAll?> { VersionIndependentResourceTypesAll.Patient };
#else
            var resourceTypes = new List<ResourceType?> { ResourceType.Patient };
#endif

            var searchParam = new SearchParameter
            {
                Id = Guid.NewGuid().ToString(),
                Url = url,
                Name = $"TestParam_{Guid.NewGuid():N}",
                Status = PublicationStatus.Active,
                Code = $"test-{Guid.NewGuid():N}".Substring(0, 20),
                Base = resourceTypes,
                Type = SearchParamType.String,
                Expression = "Patient.name",
            };

            var result = await _fixture.Mediator.UpsertResourceAsync(searchParam.ToResourceElement(), cancellationToken: cancellationToken);
            return result.RawResourceElement.Id;
        }

        /// <summary>
        /// Verify that cache sync populates the cache with parameters that have status=Supported or status=Enabled from the status table.
        /// </summary>
        [Fact]
        public async Task GivenNewStatusEntry_WhenEnabledAndCacheSynced_ThenCacheIsCorrect()
        {
            var testUri = $"http://myorg/id_{Guid.NewGuid()}";
            try
            {
                await CreateSearchParameterResourceAsync(testUri, CancellationToken.None);

                await _fixture.SearchParameterOperations.GetAndApplySearchParameterUpdates(CancellationToken.None);
                var foundSupported = _fixture.SearchParameterDefinitionManager.TryGetSearchParameter(testUri, out var param);
                Assert.True(foundSupported, "Parameter should be in cache with Supported status");
                Assert.Equal(SearchParameterStatus.Supported, param.SearchParameterStatus);

                await _fixture.SearchParameterStatusManager.UpdateSearchParameterStatusAsync(
                    [testUri],
                    SearchParameterStatus.Enabled,
                    CancellationToken.None,
                    lastUpdated: _fixture.SearchParameterOperations.SearchParamLastUpdated);

                await _fixture.SearchParameterOperations.GetAndApplySearchParameterUpdates(CancellationToken.None);
                var found = _fixture.SearchParameterDefinitionManager.TryGetSearchParameter(testUri, out param);
                Assert.True(found, $"SearchParameter {testUri} should be in cache after CacheSynced");
                Assert.NotNull(param);
                Assert.Equal(SearchParameterStatus.Enabled, param.SearchParameterStatus);
            }
            finally
            {
                await _testHelper.DeleteSearchParameterStatusAsync(testUri);
            }
        }

        /// <summary>
        /// Verify that when a search parameter status is updated to Disabled it is reflected in cache.
        /// </summary>
        [Fact]
        public async Task GivenCachedParameter_WhenDisabledAndCacheSynced_ThenCacheIsCorrect()
        {
            var testUri = $"http://myorg/id_{Guid.NewGuid()}";
            try
            {
                await CreateSearchParameterResourceAsync(testUri, CancellationToken.None);

                await _fixture.SearchParameterOperations.GetAndApplySearchParameterUpdates(CancellationToken.None);
                var found = _fixture.SearchParameterDefinitionManager.TryGetSearchParameter(testUri, out var param);
                Assert.True(found, "Parameter should be in cache with Supported status");
                Assert.Equal(SearchParameterStatus.Supported, param.SearchParameterStatus);

                await _fixture.SearchParameterStatusManager.UpdateSearchParameterStatusAsync(
                    [testUri],
                    SearchParameterStatus.Disabled,
                    CancellationToken.None,
                    lastUpdated: _fixture.SearchParameterOperations.SearchParamLastUpdated);

                await _fixture.SearchParameterOperations.GetAndApplySearchParameterUpdates(CancellationToken.None);
                found = _fixture.SearchParameterDefinitionManager.TryGetSearchParameter(testUri, out param);
                Assert.True(found, "Disabled parameter should still be in cache");
                Assert.False(param.IsSupported, "Disabled parameter should have IsSupported = false");
                Assert.Equal(SearchParameterStatus.Disabled, param.SearchParameterStatus);

                found = _fixture.SupportedSearchParameterDefinitionManager.TryGetSearchParameter(testUri, out _);
                Assert.False(found, "Disabled parameter should not be in supported cache");

                await _fixture.SearchParameterStatusManager.UpdateSearchParameterStatusAsync(
                    [testUri],
                    SearchParameterStatus.Enabled,
                    CancellationToken.None,
                    lastUpdated: _fixture.SearchParameterOperations.SearchParamLastUpdated);

                await _fixture.SearchParameterOperations.GetAndApplySearchParameterUpdates(CancellationToken.None);
                found = _fixture.SearchParameterDefinitionManager.TryGetSearchParameter(testUri, out param);
                Assert.True(found, "Enabled parameter should be in cache");
                Assert.NotNull(param);
                Assert.Equal(SearchParameterStatus.Enabled, param.SearchParameterStatus);
            }
            finally
            {
                await _testHelper.DeleteSearchParameterStatusAsync(testUri);
            }
        }

        /// <summary>
        /// Verify that when a search param status transitions to Deleted cache sync removes it from the cache.
        /// </summary>
        [Fact]
        public async Task GivenCachedParameter_WhenDeletedAndCacheSynced_ThenCacheIsCorrect()
        {
            var testUri = $"http://myorg/id_{Guid.NewGuid()}";
            try
            {
                await CreateSearchParameterResourceAsync(testUri, CancellationToken.None);

                await _fixture.SearchParameterOperations.GetAndApplySearchParameterUpdates(CancellationToken.None);
                var found = _fixture.SearchParameterDefinitionManager.TryGetSearchParameter(testUri, out var param);
                Assert.True(found, "Parameter should be in cache with Supported status");
                Assert.Equal(SearchParameterStatus.Supported, param.SearchParameterStatus);

                await _fixture.SearchParameterStatusManager.UpdateSearchParameterStatusAsync(
                    [testUri],
                    SearchParameterStatus.Enabled,
                    CancellationToken.None,
                    lastUpdated: _fixture.SearchParameterOperations.SearchParamLastUpdated);

                await _fixture.SearchParameterOperations.GetAndApplySearchParameterUpdates(CancellationToken.None);
                found = _fixture.SearchParameterDefinitionManager.TryGetSearchParameter(testUri, out param);
                Assert.True(found, "Parameter should be in cache");
                Assert.Equal(SearchParameterStatus.Enabled, param.SearchParameterStatus);

                await _fixture.SearchParameterStatusManager.UpdateSearchParameterStatusAsync(
                    [testUri],
                    SearchParameterStatus.Deleted,
                    CancellationToken.None,
                    lastUpdated: _fixture.SearchParameterOperations.SearchParamLastUpdated);

                await _fixture.SearchParameterOperations.GetAndApplySearchParameterUpdates(CancellationToken.None);
                Assert.False(_fixture.SearchParameterDefinitionManager.TryGetSearchParameter(testUri, out _), "Deleted parameter should not be in cache");
            }
            finally
            {
                await _testHelper.DeleteSearchParameterStatusAsync(testUri);
            }
        }

        /// <summary>
        /// Verify that cache sync correctly handles a large number of search parameters by batching the URL lookups and processing all parameters.
        /// </summary>
        [Fact]
        public async Task GivenLargeNumberOfParameters_WhenCacheSynced_ThenAllAreCached()
        {
            const int count = 160; // this should exceed the batch size used in GetSearchParametersByUrlsAsync (currently 100)
            var urls = new List<string>();

            try
            {
                for (int i = 0; i < count; i++)
                {
                    // Create URL with maximum supported length (128 chars)
                    // Format: http://myorg/id_XXXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX_padding... (128 chars total)
                    var guid = Guid.NewGuid();
                    var baseUrl = $"http://myorg/id_{guid}";
                    var padding = new string('x', 128 - baseUrl.Length);
                    var url = baseUrl + padding;
                    urls.Add(url);

                    await CreateSearchParameterResourceAsync(url, CancellationToken.None);
                }

                await _fixture.SearchParameterOperations.GetAndApplySearchParameterUpdates(CancellationToken.None);

                int supportedCount = 0;
                foreach (var url in urls)
                {
                    if (_fixture.SearchParameterDefinitionManager.TryGetSearchParameter(url, out var param))
                    {
                        Assert.Equal(SearchParameterStatus.Supported, param.SearchParameterStatus);
                        supportedCount++;
                    }
                }

                Assert.Equal(count, supportedCount);

                await _fixture.SearchParameterStatusManager.UpdateSearchParameterStatusAsync(
                    urls,
                    SearchParameterStatus.Enabled,
                    CancellationToken.None,
                    lastUpdated: _fixture.SearchParameterOperations.SearchParamLastUpdated);

                await _fixture.SearchParameterOperations.GetAndApplySearchParameterUpdates(CancellationToken.None);

                int cachedCount = 0;
                foreach (var url in urls.Where(url => _fixture.SearchParameterDefinitionManager.TryGetSearchParameter(url, out _)))
                {
                    _fixture.SearchParameterDefinitionManager.TryGetSearchParameter(url, out var param);
                    Assert.Equal(SearchParameterStatus.Enabled, param.SearchParameterStatus);
                    cachedCount++;
                }

                Assert.Equal(count, cachedCount);
            }
            finally
            {
                foreach (var url in urls)
                {
                    await _testHelper.DeleteSearchParameterStatusAsync(url);
                }
            }
        }
    }
}
