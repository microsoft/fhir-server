// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Storage
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Security)]
    public sealed class FhirMemoryCacheTests
    {
        private readonly ILogger _logger = Substitute.For<ILogger>();

        private const int Megabyte = 1 * 1024 * 1024;
        private const string DefaultKey = "key";
        private const string DefaultValue = "value";

        [Theory]
        [InlineData(01 * Megabyte)]
        [InlineData(14 * Megabyte)]
        [InlineData(55 * Megabyte)]
        public void GivenAnEmptyCache_CheckTheCacheMemoryLimit(long expectedLimitSizeInBytes)
        {
            var cache = new FhirMemoryCache<string>(name: "cache", sizeLimit: expectedLimitSizeInBytes, entryExpirationTime: TimeSpan.FromMinutes(1), _logger);

            Assert.Equal(expectedLimitSizeInBytes, cache.CacheMemoryLimit);
        }

        [Fact]
        public void GivenACacheWithCompressionPercentageZero_DoNotAddMoreElements_WhenTheTotalNumberCacheLimitIsReached()
        {
            // In this test, a few behaviors are validated:
            // - Compaction percentage is set to ZERO - as a validation, a ZERO compaction percentage should not reduce the cache size once is full.
            // - No more elements should be added after the max cache size is reached.
            // - At the end of the test all elements should still be present in cache, even if with different priorities.

            const int maxNumberOfAttempts = 15000;
            const long maxCacheSize = Megabyte;

            var cache = new FhirMemoryCache<int>(
                name: "cache",
                sizeLimit: maxCacheSize,
                entryExpirationTime: TimeSpan.FromMinutes(10),
                _logger,
                compactionPercentage: 0);

            long totalSizeAddedToCache = 0;
            long totalSizeAttemptedToBeAddedToCache = 0;
            int ingestedElements = 0;
            int notIngestedElements = 0;

            for (var i = 0; i < maxNumberOfAttempts; i++)
            {
                string key = Guid.NewGuid().ToString();
                int newEntrySize = ASCIIEncoding.Unicode.GetByteCount(key) + sizeof(int);

                bool ingested = cache.TryAdd(key, 2112, (FhirMemoryCacheItemPriority)(i % 4));

                totalSizeAttemptedToBeAddedToCache += newEntrySize;

                if (ingested)
                {
                    if (notIngestedElements > 0)
                    {
                        // Ensuring that, once an element is refused, no new elements are added.
                        // As this is a test, and all elements have the same size, this is an expected behavior.
                        // But in real production scenarios, as the size of elements can vary and the compression percentage is not zero, then new elements could be added.
                        Assert.Fail("Once the cache refuses elements (due the total size reached) then no new elements should be added");
                    }

                    ingestedElements++;
                    totalSizeAddedToCache += newEntrySize;
                }
                else
                {
                    notIngestedElements++;

                    // Ensure requests to non-existing values do not crash.
                    Assert.Equal(default(int), cache.Get(key));
                    Assert.False(cache.TryGet(key, out int defaultValue));

                    // Validates the attempts vs limit.
                    Assert.True(totalSizeAttemptedToBeAddedToCache > cache.CacheMemoryLimit, "If element is rejected, then total size attempted to be added must be higher than cache memory limit.");
                }
            }

            Assert.True(ingestedElements == cache.Count, "Ingested elements should be equal to cache size.");
            Assert.True(ingestedElements < maxNumberOfAttempts, "In this test not all elements should be ingested, as the memory limit should be reached.");
            Assert.True(ingestedElements + notIngestedElements == maxNumberOfAttempts);
            Assert.True(totalSizeAddedToCache <= maxCacheSize, "In this test the total size added to cache should be lower than the max allowed cache size.");
        }

        [Fact]
        public void GivenACache_RaiseErrorsIfParametersAreInvalid()
        {
            Assert.Throws<ArgumentNullException>(
                () => new FhirMemoryCache<string>(
                    null,
                    sizeLimit: 0,
                    TimeSpan.FromMinutes(1),
                    _logger));

            Assert.Throws<ArgumentOutOfRangeException>(
                () => new FhirMemoryCache<string>(
                    Guid.NewGuid().ToString(),
                    sizeLimit: 0,
                    TimeSpan.FromMinutes(1),
                    _logger));

            Assert.Throws<ArgumentNullException>(
                () => new FhirMemoryCache<string>(
                    Guid.NewGuid().ToString(),
                    sizeLimit: Megabyte,
                    TimeSpan.FromMinutes(1),
                    null));

            var cache = CreateRegularMemoryCache<string>();

            Assert.Throws<ArgumentNullException>(() => cache.GetOrAdd(null, DefaultValue));
            Assert.Throws<ArgumentNullException>(() => cache.TryAdd(null, DefaultValue));
            Assert.Throws<ArgumentException>(() => cache.GetOrAdd(string.Empty, DefaultValue));
            Assert.Throws<ArgumentException>(() => cache.TryAdd(string.Empty, DefaultValue));
            Assert.Throws<ArgumentNullException>(() => cache.GetOrAdd(DefaultKey, null));
            Assert.Throws<ArgumentNullException>(() => cache.TryAdd(DefaultKey, null));
        }

        [Fact]
        public void GivenAnEmptyCache_WhenAddingValue_ThenValueShouldBeAdded()
        {
            var cache = CreateRegularMemoryCache<string>();

            var result1 = cache.GetOrAdd(DefaultKey, DefaultValue);
            Assert.Equal(DefaultValue, result1);

            const string anotherValue = "Another Value";
            var result2 = cache.GetOrAdd(DefaultKey, anotherValue);
            Assert.NotEqual(anotherValue, result2);
            Assert.Equal(DefaultValue, result1);
        }

        [Fact]
        public void GivenAnEmptyCache_WhenAddingValue_ThenValueShouldBeAddedAndCanBeRetrieved()
        {
            var cache = CreateRegularMemoryCache<string>();

            cache.GetOrAdd(DefaultKey, DefaultValue);

            Assert.True(cache.TryGet(DefaultKey, out var result));
            Assert.Equal(DefaultValue, result);
        }

        [Fact]
        public void GivenAnEmptyCache_WhenAddingValueIfIgnoreCaseEnabled_ThenMultipleSimilarKeysShouldWorkAsExpected()
        {
            var cache = new FhirMemoryCache<string>(Guid.NewGuid().ToString(), sizeLimit: Megabyte, TimeSpan.FromMinutes(1), _logger, ignoreCase: true);

            cache.GetOrAdd(DefaultKey, DefaultValue);

            Assert.True(cache.TryGet(DefaultKey, out var result));
            Assert.Equal(DefaultValue, result);

            Assert.True(cache.TryGet(DefaultKey.ToUpper(), out result));
            Assert.Equal(DefaultValue, result);

            Assert.True(cache.TryGet("Key", out result));
            Assert.Equal(DefaultValue, result);

            Assert.True(cache.TryGet("kEy", out result));
            Assert.Equal(DefaultValue, result);
        }

        [Fact]
        public void GivenAnEmptyCache_WhenGettingAnItemThatDoNotExist_ThenReturnFalse()
        {
            var cache = CreateRegularMemoryCache<string>();

            Assert.False(cache.TryGet(DefaultKey, out var result));
            Assert.Equal(default, result);
        }

        [Fact]
        public void GivenAnEmptyCache_WhenAddingValueIfIgnoreCaseDisabled_ThenMultipleSimilarKeysShouldWorkAsExpected()
        {
            var cache = CreateRegularMemoryCache<string>();

            cache.GetOrAdd(DefaultKey, DefaultValue);

            Assert.True(cache.TryGet("key", out var result));
            Assert.Equal(DefaultValue, result);

            Assert.False(cache.TryGet("KEY", out result));

            Assert.False(cache.TryGet("Key", out result));

            Assert.False(cache.TryGet("kEy", out result));
        }

        [Fact]
        public void GivenAnEmptyCache_WhenAddingValue_ThenValueShouldBeAddedAndCanBeRetrievedUsingTryGetValue()
        {
            var cache = CreateRegularMemoryCache<string>();

            cache.GetOrAdd(DefaultKey, DefaultValue);

            Assert.True(cache.TryGet(DefaultKey, out var result));
            Assert.Equal(DefaultValue, result);
            Assert.Equal(1, cache.Count);
        }

        [Fact]
        public void GivenAnEmptyCache_WhenAddingValue_ThenValueShouldBeAddedAndCanBeRetrievedUsingGet()
        {
            var cache = CreateRegularMemoryCache<string>();

            cache.GetOrAdd(DefaultKey, DefaultValue);

            Assert.NotNull(cache.Get(DefaultKey));
            Assert.Equal(DefaultValue, cache.Get(DefaultKey));
            Assert.Equal(1, cache.Count);
        }

        [Fact]
        public void GivenAnEmptyCache_WorkForSupportedTypes()
        {
            var cache1 = CreateRegularMemoryCache<string>();
            cache1.TryAdd(DefaultKey, "foo");
            Assert.Equal(1, cache1.Count);

            var cache2 = CreateRegularMemoryCache<int>();
            cache2.TryAdd(DefaultKey, 1);
            Assert.Equal(1, cache2.Count);

            var cache3 = CreateRegularMemoryCache<long>();
            cache3.TryAdd(DefaultKey, 1);
            Assert.Equal(1, cache3.Count);

            var cache4 = CreateRegularMemoryCache<byte[]>();
            cache4.TryAdd(DefaultKey, new byte[] { 0 });
            Assert.Equal(1, cache4.Count);
        }

        [Fact]
        public void GivenAnEmptyCache_WhenRunningOperations_ThenItemsShouldBeRespected()
        {
            var cache = CreateRegularMemoryCache<long>();

            cache.GetOrAdd(DefaultKey, 2112);
            Assert.True(cache.TryGet(DefaultKey, out var result));
            Assert.Equal(2112, result);

            Assert.True(cache.Remove(DefaultKey));

            Assert.False(cache.TryGet(DefaultKey, out result));
        }

        private IFhirMemoryCache<T> CreateRegularMemoryCache<T>()
        {
            return new FhirMemoryCache<T>(name: "cache", sizeLimit: Megabyte, entryExpirationTime: TimeSpan.FromMinutes(10), _logger);
        }
    }
}
