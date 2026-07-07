// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.SqlServer.Features.Search;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search
{
    /// <summary>
    /// Unit tests for the diagnostic Query Store circuit breaker in <see cref="SqlServerSearchService"/>.
    /// The breaker suspends Query Store enrichment after a run of consecutive failures so a truly
    /// overloaded database is not compounded by diagnostic load, while slow-query warnings keep flowing.
    /// The breaker state is process-global static, so these tests reset it before each case and run
    /// sequentially within this single class.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class SqlServerSearchServiceCircuitBreakerTests
    {
        public SqlServerSearchServiceCircuitBreakerTests()
        {
            // RecordQueryStoreSuccess zeroes both the consecutive-failure counter and the open deadline,
            // giving every test a clean, closed breaker regardless of prior test state.
            SqlServerSearchService.RecordQueryStoreSuccess();
        }

        [Fact]
        public void GivenClosedBreaker_WhenTryEnter_ThenReturnsTrue()
        {
            Assert.True(SqlServerSearchService.TryEnterQueryStoreCircuit());
        }

        [Theory]
        [InlineData(SqlServerSearchService.QueryStoreCircuitBreakerFailureThreshold - 1, true)] // just below threshold: breaker stays closed
        [InlineData(SqlServerSearchService.QueryStoreCircuitBreakerFailureThreshold, false)] // reaches threshold: breaker opens
        public void GivenConsecutiveFailures_WhenTryEnter_ThenBreakerOpensOnlyAtThreshold(int failureCount, bool expectedCanEnter)
        {
            for (int i = 0; i < failureCount; i++)
            {
                SqlServerSearchService.RecordQueryStoreFailure();
            }

            Assert.Equal(expectedCanEnter, SqlServerSearchService.TryEnterQueryStoreCircuit());
        }

        [Fact]
        public void GivenFailuresBelowThreshold_WhenSuccessRecorded_ThenFailureCountResets()
        {
            // Accumulate failures just short of tripping, record a success, then accumulate the same
            // number again. Because the success reset the counter, the breaker must remain closed.
            for (int i = 0; i < SqlServerSearchService.QueryStoreCircuitBreakerFailureThreshold - 1; i++)
            {
                SqlServerSearchService.RecordQueryStoreFailure();
            }

            SqlServerSearchService.RecordQueryStoreSuccess();

            for (int i = 0; i < SqlServerSearchService.QueryStoreCircuitBreakerFailureThreshold - 1; i++)
            {
                SqlServerSearchService.RecordQueryStoreFailure();
            }

            Assert.True(SqlServerSearchService.TryEnterQueryStoreCircuit());
        }

        [Fact]
        public void GivenOpenBreaker_WhenSuccessRecorded_ThenBreakerCloses()
        {
            for (int i = 0; i < SqlServerSearchService.QueryStoreCircuitBreakerFailureThreshold; i++)
            {
                SqlServerSearchService.RecordQueryStoreFailure();
            }

            Assert.False(SqlServerSearchService.TryEnterQueryStoreCircuit());

            SqlServerSearchService.RecordQueryStoreSuccess();

            Assert.True(SqlServerSearchService.TryEnterQueryStoreCircuit());
        }

        [Fact]
        public void GivenOpenBreaker_WhenWithinCooldown_ThenTryEnterReturnsFalse()
        {
            SqlServerSearchService.SetQueryStoreCircuitStateForTests(
                SqlServerSearchService.QueryStoreCircuitBreakerFailureThreshold,
                DateTime.UtcNow.Add(SqlServerSearchService.QueryStoreCircuitBreakerCooldown).Ticks);

            Assert.False(SqlServerSearchService.TryEnterQueryStoreCircuit());
        }

        [Fact]
        public void GivenOpenBreaker_WhenCooldownElapsed_ThenBreakerClosesAndLookupsResume()
        {
            SqlServerSearchService.SetQueryStoreCircuitStateForTests(
                SqlServerSearchService.QueryStoreCircuitBreakerFailureThreshold,
                DateTime.UtcNow.AddSeconds(-1).Ticks);

            // Cooldown has elapsed: the first caller atomically clears the open deadline, closing the
            // breaker so lookups resume (subject to the concurrency gate).
            Assert.True(SqlServerSearchService.TryEnterQueryStoreCircuit());
            Assert.Equal(0, SqlServerSearchService.GetQueryStoreCircuitOpenUntilTicksForTests());
        }

        [Fact]
        public void GivenClosedBreakerAfterCooldown_WhenNextFailureRecorded_ThenBreakerReopens()
        {
            SqlServerSearchService.SetQueryStoreCircuitStateForTests(
                SqlServerSearchService.QueryStoreCircuitBreakerFailureThreshold,
                DateTime.UtcNow.AddSeconds(-1).Ticks);

            // Cooldown elapsed: the breaker closes and the next lookup is allowed through...
            Assert.True(SqlServerSearchService.TryEnterQueryStoreCircuit());

            // ...but because the failure counter is still at threshold, a single failure re-opens it.
            SqlServerSearchService.RecordQueryStoreFailure();

            Assert.False(SqlServerSearchService.TryEnterQueryStoreCircuit());
        }
    }
}
