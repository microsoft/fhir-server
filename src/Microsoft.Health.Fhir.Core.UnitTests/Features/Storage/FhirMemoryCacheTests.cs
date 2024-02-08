// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Storage
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Security)]
    public sealed class FhirMemoryCacheTests
    {
        [Theory]
        [InlineData(01, 01 * 1024 * 1024)]
        [InlineData(14, 14 * 1024 * 1024)]
        [InlineData(55, 55 * 1024 * 1024)]
        public void GivenAnEmptyCache_CheckTheCacheMemoryLimit(int limitSizeInMegabytes, long expectedLimitSizeInBytes)
        {
            var cache = new FhirMemoryCache<string>("test", limitSizeInMegabytes, TimeSpan.FromMinutes(1));

            Assert.Equal(expectedLimitSizeInBytes, cache.CacheMemoryLimit);
        }

        [Fact]
        public void GivenACache_RaiseErrorIfMemoryLimitIsSetToZero()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new FhirMemoryCache<string>("test", limitSizeInMegabytes: 0, TimeSpan.FromMinutes(1)));
        }

        [Fact]
        public void GivenAnEmptyCache_WhenAddingValue_ThenValueShouldBeAdded()
        {
            var cache = new FhirMemoryCache<string>("test", limitSizeInMegabytes: 1, TimeSpan.FromMinutes(1));
            var value = "value";

            var result = cache.GetOrAdd("key", value);

            Assert.Equal(value, result);
        }

        [Fact]
        public void GivenAnEmptyCache_WhenAddingValue_ThenValueShouldBeAddedAndCanBeRetrieved()
        {
            var cache = new FhirMemoryCache<string>("test", limitSizeInMegabytes: 1, TimeSpan.FromMinutes(1));
            var value = "value";

            cache.GetOrAdd("key", value);

            Assert.True(cache.TryGet("key", out var result));
            Assert.Equal(value, result);
        }

        [Fact]
        public void GivenAnEmptyCache_WhenAddingValue_ThenValueShouldBeAddedAndCanBeRetrievedUsingTryGetValue()
        {
            var cache = new FhirMemoryCache<string>("test", limitSizeInMegabytes: 1, TimeSpan.FromMinutes(1));
            var value = "value";

            cache.GetOrAdd("key", value);

            Assert.True(cache.TryGet("key", out var result));
            Assert.Equal(value, result);
        }

        [Fact]
        public void GivenAnEmptyCache_WhenAddingValue_ThenValueShouldBeAddedAndCanBeRetrievedUsingTryGetValueWithOut()
        {
            var cache = new FhirMemoryCache<string>("test", limitSizeInMegabytes: 1, TimeSpan.FromMinutes(1));
            var value = "value";

            cache.GetOrAdd("key", value);

            Assert.True(cache.TryGet("key", out var result));
            Assert.Equal(value, result);
        }

        [Fact]
        public void GivenAnEmptyCache_WhenAddingValue_ThenValueShouldBeAddedAndCanBeRetrievedUsingTryGetValueWithOutAndValue()
        {
            var cache = new FhirMemoryCache<string>("test", limitSizeInMegabytes: 1, TimeSpan.FromMinutes(1));
            var value = "value";

            cache.GetOrAdd("key", value);

            Assert.True(cache.TryGet("key", out var result));
            Assert.Equal(value, result);
        }

        [Fact]
        public void GivenAnEmptyCache_WhenRunningOperations_ThenItemsShouldBeRespected()
        {
            var cache = new FhirMemoryCache<long>("test", limitSizeInMegabytes: 1, TimeSpan.FromMinutes(1));

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

            var cache = new FhirMemoryCache<int>("test", limitSizeInMegabytes: 1, TimeSpan.FromMinutes(1));
            cache.AddRange(originalValues);

            foreach (var item in originalValues)
            {
                Assert.Equal(item.Value, cache.Get(item.Key));
            }
        }
    }
}
