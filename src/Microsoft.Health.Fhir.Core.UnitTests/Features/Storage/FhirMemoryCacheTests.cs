// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
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

        [Theory]
        [InlineData(01, 01 * 1024 * 1024)]
        [InlineData(14, 14 * 1024 * 1024)]
        [InlineData(55, 55 * 1024 * 1024)]
        public void GivenAnEmptyCache_CheckTheCacheMemoryLimit(int limitSizeInMegabytes, long expectedLimitSizeInBytes)
        {
            var cache = new FhirMemoryCache<string>(Guid.NewGuid().ToString(), limitSizeInMegabytes, TimeSpan.FromMinutes(1), _logger);

            Assert.Equal(expectedLimitSizeInBytes, cache.CacheMemoryLimit);
        }

        [Fact]
        public void GivenACache_RaiseErrorIfMemoryLimitIsSetToZero()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new FhirMemoryCache<string>("test 2", limitSizeInMegabytes: 0, TimeSpan.FromMinutes(1), _logger));
        }

        [Fact]
        public void GivenAnEmptyCache_WhenAddingValue_ThenValueShouldBeAdded()
        {
            var cache = new FhirMemoryCache<string>(Guid.NewGuid().ToString(), limitSizeInMegabytes: 1, TimeSpan.FromMinutes(1), _logger);
            var value = "value";

            var result = cache.GetOrAdd("key", value);

            Assert.Equal(value, result);
        }

        [Fact]
        public void GivenAnEmptyCache_WhenAddingValue_ThenValueShouldBeAddedAndCanBeRetrieved()
        {
            var cache = new FhirMemoryCache<string>(Guid.NewGuid().ToString(), limitSizeInMegabytes: 1, TimeSpan.FromMinutes(1), _logger);
            var value = "value";

            cache.GetOrAdd("key", value);

            Assert.True(cache.TryGet("key", out var result));
            Assert.Equal(value, result);
        }

        [Fact]
        public void GivenAnEmptyCache_WhenAddingValueIfIgnoreCaseEnabled_ThenMultipleSimilarKeysShouldWorkAsExpected()
        {
            var cache = new FhirMemoryCache<string>(Guid.NewGuid().ToString(), limitSizeInMegabytes: 1, TimeSpan.FromMinutes(1), _logger, ignoreCase: true);
            var value = "value";

            cache.GetOrAdd("key", value);

            Assert.True(cache.TryGet("key", out var result));
            Assert.Equal(value, result);

            Assert.True(cache.TryGet("KEY", out result));
            Assert.Equal(value, result);

            Assert.True(cache.TryGet("Key", out result));
            Assert.Equal(value, result);

            Assert.True(cache.TryGet("kEy", out result));
            Assert.Equal(value, result);
        }

        [Fact]
        public void GivenAnEmptyCache_WhenAddingValueIfIgnoreCaseDisabled_ThenMultipleSimilarKeysShouldWorkAsExpected()
        {
            var cache = new FhirMemoryCache<string>(Guid.NewGuid().ToString(), limitSizeInMegabytes: 1, TimeSpan.FromMinutes(1), _logger, ignoreCase: false);
            var value = "value";

            cache.GetOrAdd("key", value);

            Assert.True(cache.TryGet("key", out var result));
            Assert.Equal(value, result);

            Assert.False(cache.TryGet("KEY", out result));

            Assert.False(cache.TryGet("Key", out result));

            Assert.False(cache.TryGet("kEy", out result));
        }

        [Fact]
        public void GivenAnEmptyCache_WhenAddingValue_ThenValueShouldBeAddedAndCanBeRetrievedUsingTryGetValue()
        {
            var cache = new FhirMemoryCache<string>(Guid.NewGuid().ToString(), limitSizeInMegabytes: 1, TimeSpan.FromMinutes(1), _logger);
            var value = "value";

            cache.GetOrAdd("key", value);

            Assert.True(cache.TryGet("key", out var result));
            Assert.Equal(value, result);
        }

        [Fact]
        public void GivenAnEmptyCache_WhenAddingValue_ThenValueShouldBeAddedAndCanBeRetrievedUsingTryGetValueWithOut()
        {
            var cache = new FhirMemoryCache<string>(Guid.NewGuid().ToString(), limitSizeInMegabytes: 1, TimeSpan.FromMinutes(1), _logger);
            var value = "value";

            cache.GetOrAdd("key", value);

            Assert.True(cache.TryGet("key", out var result));
            Assert.Equal(value, result);
        }

        [Fact]
        public void GivenAnEmptyCache_WhenAddingValue_ThenValueShouldBeAddedAndCanBeRetrievedUsingTryGetValueWithOutAndValue()
        {
            var cache = new FhirMemoryCache<string>(Guid.NewGuid().ToString(), limitSizeInMegabytes: 1, TimeSpan.FromMinutes(1), _logger);
            var value = "value";

            cache.GetOrAdd("key", value);

            Assert.True(cache.TryGet("key", out var result));
            Assert.Equal(value, result);
        }

        [Fact]
        public void GivenAnEmptyCache_WhenRunningOperations_ThenItemsShouldBeRespected()
        {
            var cache = new FhirMemoryCache<long>(Guid.NewGuid().ToString(), limitSizeInMegabytes: 1, TimeSpan.FromMinutes(1), _logger);

            cache.GetOrAdd("key", 2112);
            Assert.True(cache.TryGet("key", out var result));
            Assert.Equal(2112, result);

            Assert.True(cache.Remove("key"));

            Assert.False(cache.TryGet("key", out result));
        }

        [Fact]
        public void GivenAnEmptyCache_WhenAddingARange_AllValuesShouldBeIngested()
        {
            int anchor = 'a';
            var originalValues = new Dictionary<string, int>();
            for (int i = 0; i < 20; i++)
            {
                originalValues.Add(((char)(anchor + i)).ToString(), i);
            }

            var cache = new FhirMemoryCache<int>(Guid.NewGuid().ToString(), limitSizeInMegabytes: 1, TimeSpan.FromMinutes(1), _logger);
            cache.AddRange(originalValues);

            foreach (var item in originalValues)
            {
                Assert.Equal(item.Value, cache.Get(item.Key));
            }
        }
    }
}
