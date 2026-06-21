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
        /// Verify that CacheSynced populates the cache with parameters
        /// that have status=Supported or status=Enabled from the status table.
        /// </summary>
        [Fact]
        public async Task GivenNewStatusEntry_WhenCacheSynced_ThenParameterIsPopulated()
        {
            await _fixture.SearchParameterOperations.GetAndApplySearchParameterUpdates(CancellationToken.None);

            var testUri = $"http://myorg/id_{Guid.NewGuid()}";
            try
            {
                await CreateSearchParameterResourceAsync(testUri, CancellationToken.None);
                await _fixture.SqlHelper.ExecuteSqlCmd($"UPDATE dbo.SearchParam SET Status = 'Enabled', LastUpdated = convert(datetimeoffset(7), sysutcdatetime()) WHERE Uri = '{testUri}'");
                await _fixture.SearchParameterOperations.GetAndApplySearchParameterUpdates(CancellationToken.None);

                var found = _fixture.SearchParameterDefinitionManager.TryGetSearchParameter(testUri, out var cachedParam);
                Assert.True(found, $"SearchParameter {testUri} should be in cache after CacheSynced");
                Assert.NotNull(cachedParam);
                Assert.Equal(SearchParameterStatus.Enabled, cachedParam.SearchParameterStatus);
            }
            finally
            {
                await _testHelper.DeleteSearchParameterStatusAsync(testUri);
            }
        }

        /// <summary>
        /// Verify that when a SearchParameter status is updated in the status table,
        /// CacheSynced refreshes the cache.
        /// </summary>
        [Fact]
        public async Task GivenCachedParameter_WhenStatusUpdatedAndCacheSynced_ThenCacheIsRefreshed()
        {
            await _fixture.SearchParameterOperations.GetAndApplySearchParameterUpdates(CancellationToken.None);

            var testUri = $"http://myorg/id_{Guid.NewGuid()}";
            try
            {
                await CreateSearchParameterResourceAsync(testUri, CancellationToken.None);

                await _fixture.SqlHelper.ExecuteSqlCmd($"UPDATE dbo.SearchParam SET Status = 'Disabled', LastUpdated = convert(datetimeoffset(7), sysutcdatetime()) WHERE Uri = '{testUri}'");
                await _fixture.SearchParameterOperations.GetAndApplySearchParameterUpdates(CancellationToken.None);

                var foundDisabled = _fixture.SearchParameterDefinitionManager.TryGetSearchParameter(testUri, out _);
                Assert.False(foundDisabled, "Disabled parameter should not be in cache");

                await _fixture.SqlHelper.ExecuteSqlCmd($"UPDATE dbo.SearchParam SET Status = 'Enabled', LastUpdated = convert(datetimeoffset(7), sysutcdatetime()) WHERE Uri = '{testUri}'");
                await _fixture.SearchParameterOperations.GetAndApplySearchParameterUpdates(CancellationToken.None);

                var foundEnabled = _fixture.SearchParameterDefinitionManager.TryGetSearchParameter(testUri, out var cachedParam);
                Assert.True(foundEnabled, "Enabled parameter should be in cache");
                Assert.NotNull(cachedParam);
                Assert.Equal(SearchParameterStatus.Enabled, cachedParam.SearchParameterStatus);
            }
            finally
            {
                await _testHelper.DeleteSearchParameterStatusAsync(testUri);
            }
        }

        /// <summary>
        /// Verify that when a SearchParameter status transitions to Deleted,
        /// CacheSynced removes it from the cache.
        /// </summary>
        [Fact]
        public async Task GivenCachedParameter_WhenStatusTransitionsToDeletedAndCacheSynced_ThenRemovedFromCache()
        {
            await _fixture.SearchParameterOperations.GetAndApplySearchParameterUpdates(CancellationToken.None);

            var testUri = $"http://myorg/id_{Guid.NewGuid()}";
            try
            {
                await CreateSearchParameterResourceAsync(testUri, CancellationToken.None);
                await _fixture.SqlHelper.ExecuteSqlCmd($"UPDATE dbo.SearchParam SET Status = 'Enabled', LastUpdated = convert(datetimeoffset(7), sysutcdatetime()) WHERE Uri = '{testUri}'");

                await _fixture.SearchParameterOperations.GetAndApplySearchParameterUpdates(CancellationToken.None);

                var foundEnabled = _fixture.SearchParameterDefinitionManager.TryGetSearchParameter(testUri, out var enabledParam);
                Assert.True(foundEnabled, "Parameter should be in cache");
                Assert.Equal(SearchParameterStatus.Enabled, enabledParam.SearchParameterStatus);

                await _fixture.SqlHelper.ExecuteSqlCmd($"UPDATE dbo.SearchParam SET Status = 'Deleted', LastUpdated = convert(datetimeoffset(7), sysutcdatetime()) WHERE Uri = '{testUri}'");
                await _fixture.SearchParameterOperations.GetAndApplySearchParameterUpdates(CancellationToken.None);

                Assert.False(_fixture.SearchParameterDefinitionManager.TryGetSearchParameter(testUri, out _), "Deleted parameter should not be in cache");
            }
            finally
            {
                await _testHelper.DeleteSearchParameterStatusAsync(testUri);
            }
        }

        /// <summary>
        /// Verify that CacheSynced correctly handles a large number of search parameters
        /// by batching the URL lookups and processing all parameters.
        /// </summary>
        [Fact]
        public async Task GivenLargeNumberOfParameters_WhenCacheSynced_ThenAllAreCached()
        {
            await _fixture.SearchParameterOperations.GetAndApplySearchParameterUpdates(CancellationToken.None);

            const int count = 105;
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

                var urlList = string.Join("','", urls);
                await _fixture.SqlHelper.ExecuteSqlCmd($"UPDATE dbo.SearchParam SET Status = 'Enabled', LastUpdated = convert(datetimeoffset(7), sysutcdatetime()) WHERE Uri IN ('{urlList}')");

                await _fixture.SearchParameterOperations.GetAndApplySearchParameterUpdates(CancellationToken.None);

                int cachedCount = 0;
                foreach (var url in urls)
                {
                    if (_fixture.SearchParameterDefinitionManager.TryGetSearchParameter(url, out _))
                    {
                        cachedCount++;
                    }
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
