// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.SqlServer.Features.Search.QueryPlanCache;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search.QueryPlanCache
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public sealed class QueryPlanCacheDynamicSelectorTests
    {
        private readonly Microsoft.Extensions.Logging.ILogger<QueryPlanCacheDynamicSelector> _logger = NullLogger<QueryPlanCacheDynamicSelector>.Instance;
        private readonly IQueryPlanCacheLoader _queryPlanCacheLoader = new FakeQueryPlanCacheLoader(isEnabled: false);

        [Fact]
        public void GivenExecutionTimes_WhereSettingsTRUEHasABetterPerformance_ThenSelectorShouldPreferTrue()
        {
            // This test simulates the case then SQL Query Plan Caching is beneficial.

            QueryPlanCacheDynamicSelector selector = new QueryPlanCacheDynamicSelector(_queryPlanCacheLoader, _logger);

            string hash = "hash1";

            // The following function simulates the execution time for each setting.
            // For even iterations (setting=true), the execution time is 50ms.
            // That will result in a faster execution, that will be learned by the selector.
            // And the selector will eventually settle on setting=true.
            var getValue = (int index) => index < 6 && index % 2 == 0 ? 100 : 50;

            for (int i = 0; i < 20; i++)
            {
                bool setting = selector.GetRecommendedQueryPlanCacheSetting(hash);

                selector.ReportExecutionTime(hash, setting, getValue(i));
            }

            Assert.True(selector.GetRecommendedQueryPlanCacheSetting(hash), "Based on the logic of this test, the selector should have settled on setting=true.");
        }

        [Fact]
        public void GivenExecutionTimes_WhereSettingsFALSEHasABetterPerformance_ThenSelectorShouldPreferTrue()
        {
            // This test simulates the case then SQL Query Plan Caching is not beneficial.

            QueryPlanCacheDynamicSelector selector = new QueryPlanCacheDynamicSelector(_queryPlanCacheLoader, _logger);

            string hash = "hash1";

            // The following function simulates the execution time for each setting.
            // For even iterations (setting=false), the execution time is 50ms.
            // That will result in a faster execution, that will be learned by the selector.
            // And the selector will eventually settle on setting=false.
            var getValue = (int index) => index > 6 || index % 2 == 0 ? 50 : 100;

            for (int i = 0; i < 20; i++)
            {
                bool setting = selector.GetRecommendedQueryPlanCacheSetting(hash);

                selector.ReportExecutionTime(hash, setting, getValue(i));
            }

            Assert.False(selector.GetRecommendedQueryPlanCacheSetting(hash), "Based on the logic of this test, the selector should have settled on setting=false.");
        }

        [Fact]
        public void GivenMultipleSequentialRequests_WithMultipleHashes_ThenTheSelectorShouldHandleIt()
        {
            QueryPlanCacheDynamicSelector selector = new QueryPlanCacheDynamicSelector(_queryPlanCacheLoader, _logger);

            string hash1 = "hash1";
            string hash2 = "hash2";
            string hash3 = "hash3";
            string hash4 = "hash4";

            for (int i = 0; i < 10; i++)
            {
                bool setting1 = selector.GetRecommendedQueryPlanCacheSetting(hash1);
                bool setting2 = selector.GetRecommendedQueryPlanCacheSetting(hash2);
                bool setting3 = selector.GetRecommendedQueryPlanCacheSetting(hash3);
                bool setting4 = selector.GetRecommendedQueryPlanCacheSetting(hash4);

                selector.ReportExecutionTime(hash1, setting1, setting1 ? 50 : 100);
                selector.ReportExecutionTime(hash2, setting2, setting2 ? 100 : 50);
                selector.ReportExecutionTime(hash3, setting3, setting3 ? 110 : 120);
                selector.ReportExecutionTime(hash4, setting4, setting4 ? 30 : 20);
            }

            Assert.True(selector.GetRecommendedQueryPlanCacheSetting(hash1));
            Assert.False(selector.GetRecommendedQueryPlanCacheSetting(hash2));
            Assert.True(selector.GetRecommendedQueryPlanCacheSetting(hash3));
            Assert.False(selector.GetRecommendedQueryPlanCacheSetting(hash4));
        }

        [Fact]
        public async Task GivenMultipleParallelRequests_WithTwoDifferentHashes_ThenTheSelectorShouldHandleIt()
        {
            QueryPlanCacheDynamicSelector selector = new QueryPlanCacheDynamicSelector(_queryPlanCacheLoader, _logger);

            string hash1 = "hash1";
            string hash2 = "hash2";
            string hash3 = "hash3";
            string hash4 = "hash4";

            StringBuilder log = new StringBuilder();

            var result = Parallel.For(0, 100, (i) =>
            {
                bool setting1 = selector.GetRecommendedQueryPlanCacheSetting(hash1);
                selector.ReportExecutionTime(hash1, setting1, setting1 ? 50 : 100);
                log.AppendLine($"Hash1: Iteration {i}, Setting={setting1}");

                bool setting2 = selector.GetRecommendedQueryPlanCacheSetting(hash2);
                selector.ReportExecutionTime(hash2, setting2, setting2 ? 100 : 50);

                bool setting3 = selector.GetRecommendedQueryPlanCacheSetting(hash3);
                selector.ReportExecutionTime(hash3, setting3, setting3 ? 110 : 120);

                bool setting4 = selector.GetRecommendedQueryPlanCacheSetting(hash4);
                selector.ReportExecutionTime(hash4, setting4, setting4 ? 30 : 20);
            });

            while (!result.IsCompleted)
            {
                await Task.Delay(10);
            }

            Assert.True(selector.GetRecommendedQueryPlanCacheSetting(hash1));
            Assert.False(selector.GetRecommendedQueryPlanCacheSetting(hash2));
            Assert.True(selector.GetRecommendedQueryPlanCacheSetting(hash3));
            Assert.False(selector.GetRecommendedQueryPlanCacheSetting(hash4));
        }

        [Fact]
        public void GivenSeveralCacheEntries_WhenReachedTheLimit_ThenTheDefaultValueShouldBeReturned()
        {
            const int maxNumberOfEntries = 500;

            QueryPlanCacheDynamicSelector selector = new QueryPlanCacheDynamicSelector(_queryPlanCacheLoader, _logger);

            string hash = string.Empty;
            bool setting = false;

            for (int i = 0; i < maxNumberOfEntries; i++)
            {
                hash = $"hash{i}";

                // Learning phase.
                setting = selector.GetRecommendedQueryPlanCacheSetting(hash);
                Assert.False(setting);
                selector.ReportExecutionTime(hash, setting, 1);

                setting = selector.GetRecommendedQueryPlanCacheSetting(hash);
                Assert.True(setting);
                selector.ReportExecutionTime(hash, setting, 1);
            }

            // After limit is reached, new entries are not allowed.
            hash = $"hash{maxNumberOfEntries + 1}";
            setting = selector.GetRecommendedQueryPlanCacheSetting(hash);
            Assert.False(setting);
            setting = selector.GetRecommendedQueryPlanCacheSetting(hash);
            Assert.False(setting);
            setting = selector.GetRecommendedQueryPlanCacheSetting(hash);
            Assert.False(setting);
            setting = selector.GetRecommendedQueryPlanCacheSetting(hash);
            Assert.False(setting);

            // After limit is reached, new entries are not allowed.
            hash = $"hash{maxNumberOfEntries + 2}";
            setting = selector.GetRecommendedQueryPlanCacheSetting(hash);
            Assert.False(setting);
            setting = selector.GetRecommendedQueryPlanCacheSetting(hash);
            Assert.False(setting);
            setting = selector.GetRecommendedQueryPlanCacheSetting(hash);
            Assert.False(setting);
            setting = selector.GetRecommendedQueryPlanCacheSetting(hash);
            Assert.False(setting);

            // While older entries are still allowed.
            for (int i = 0; i < maxNumberOfEntries; i++)
            {
                hash = $"hash{i}";

                // Learning phase.
                setting = selector.GetRecommendedQueryPlanCacheSetting(hash);
                Assert.False(setting);
                selector.ReportExecutionTime(hash, setting, 1);

                setting = selector.GetRecommendedQueryPlanCacheSetting(hash);
                Assert.True(setting);
                selector.ReportExecutionTime(hash, setting, 1);
            }
        }
    }
}
