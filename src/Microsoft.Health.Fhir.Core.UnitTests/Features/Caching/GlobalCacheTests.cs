// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Health.Fhir.Core.Features.Caching;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Caching
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Caching)]
    public class GlobalCacheTests
    {
        [Fact]
        public async Task GivenAValueWasLoaded_WhenReadAgain_ThenItIsNotReloaded()
        {
            var cache = new CountingCache();

            Assert.Equal(1, cache.Get());
            Assert.Equal(1, await cache.GetAsync());
            Assert.Equal([false], cache.Loads);
        }

        [Fact]
        public async Task GivenAnExpiry_WhenItElapses_ThenTheValueIsReloaded()
        {
            var time = new FakeTimeProvider();
            var cache = new CountingCache(TimeSpan.FromMinutes(10), time);

            Assert.Equal(1, await cache.GetAsync());
            time.Advance(TimeSpan.FromMinutes(10));
            Assert.Equal(2, await cache.GetAsync());
            Assert.Equal([true, true], cache.Loads);
        }

        private sealed class CountingCache : GlobalCache<int>
        {
            public CountingCache(TimeSpan? expiry = null, TimeProvider timeProvider = null)
                : base(NullLogger.Instance, expiry, timeProvider)
            {
            }

            public List<bool> Loads { get; } = [];

            protected override ValueTask<int> GetRemoteDataAsync(bool async, CancellationToken cancellationToken)
            {
                Loads.Add(async);
                return new ValueTask<int>(Loads.Count);
            }
        }
    }
}
