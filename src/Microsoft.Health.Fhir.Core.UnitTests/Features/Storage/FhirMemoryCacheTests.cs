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

        private const string DefaultKey = "key";
        private const string DefaultValue = "value";

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
        public void GivenACache_RaiseErrorsIfParametersAreInvalid()
        {
            Assert.Throws<ArgumentNullException>(
                () => new FhirMemoryCache<string>(
                    null,
                    limitSizeInMegabytes: 0,
                    TimeSpan.FromMinutes(1),
                    _logger));

            Assert.Throws<ArgumentOutOfRangeException>(
                () => new FhirMemoryCache<string>(
                    Guid.NewGuid().ToString(),
                    limitSizeInMegabytes: 0,
                    TimeSpan.FromMinutes(1),
                    _logger));

            Assert.Throws<ArgumentNullException>(
                () => new FhirMemoryCache<string>(
                    Guid.NewGuid().ToString(),
                    limitSizeInMegabytes: 1,
                    TimeSpan.FromMinutes(1),
                    null));
        }

        [Fact]
        public void GivenAnEmptyCache_WhenAddingValue_ThenValueShouldBeAdded()
        {
            var cache = CreateRegularMemoryCache<string>();

            var result = cache.GetOrAdd(DefaultKey, DefaultValue);

            Assert.Equal(DefaultValue, result);
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
            var cache = new FhirMemoryCache<string>(Guid.NewGuid().ToString(), limitSizeInMegabytes: 1, TimeSpan.FromMinutes(1), _logger, ignoreCase: true);

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

            Assert.True(cache.TryGet("key", out var result));
            Assert.Equal(DefaultValue, result);
        }

        [Fact]
        public void GivenAnEmptyCache_WhenAddingValue_ThenValueShouldBeAddedAndCanBeRetrievedUsingTryGetValueWithOut()
        {
            var cache = CreateRegularMemoryCache<string>();

            cache.GetOrAdd(DefaultKey, DefaultValue);

            Assert.True(cache.TryGet("key", out var result));
            Assert.Equal(DefaultValue, result);
        }

        [Fact]
        public void GivenAnEmptyCache_WhenAddingValue_ThenValueShouldBeAddedAndCanBeRetrievedUsingTryGetValueWithOutAndValue()
        {
            var cache = CreateRegularMemoryCache<string>();

            cache.GetOrAdd(DefaultKey, DefaultValue);

            Assert.True(cache.TryGet(DefaultKey, out var result));
            Assert.Equal(DefaultValue, result);
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

        [Fact]
        public void GivenAnEmptyCache_WhenAddingARange_AllValuesShouldBeIngested()
        {
            int anchor = 'a';
            var originalValues = new Dictionary<string, int>();
            for (int i = 0; i < 20; i++)
            {
                originalValues.Add(((char)(anchor + i)).ToString(), i);
            }

            var cache = CreateRegularMemoryCache<int>();
            cache.AddRange(originalValues);

            foreach (var item in originalValues)
            {
                Assert.Equal(item.Value, cache.Get(item.Key));
            }
        }

        private IMemoryCache<T> CreateRegularMemoryCache<T>()
        {
            return new FhirMemoryCache<T>(Guid.NewGuid().ToString(), limitSizeInMegabytes: 1, TimeSpan.FromMinutes(1), _logger);
        }
    }
}
