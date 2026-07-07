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

        [Fact]
        public void GivenFailuresBelowThreshold_WhenTryEnter_ThenBreakerStaysClosed()
        {
            for (int i = 0; i < SqlServerSearchService.QueryStoreCircuitBreakerFailureThreshold - 1; i++)
            {
                SqlServerSearchService.RecordQueryStoreFailure();
            }

            Assert.True(SqlServerSearchService.TryEnterQueryStoreCircuit());
        }

        [Fact]
        public void GivenFailuresAtThreshold_WhenTryEnter_ThenBreakerOpens()
        {
            for (int i = 0; i < SqlServerSearchService.QueryStoreCircuitBreakerFailureThreshold; i++)
            {
                SqlServerSearchService.RecordQueryStoreFailure();
            }

            Assert.False(SqlServerSearchService.TryEnterQueryStoreCircuit());
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
        public void GivenOpenBreaker_WhenCooldownElapsed_ThenSingleProbeIsAllowedThrough()
        {
            SqlServerSearchService.SetQueryStoreCircuitStateForTests(
                SqlServerSearchService.QueryStoreCircuitBreakerFailureThreshold,
                DateTime.UtcNow.AddSeconds(-1).Ticks);

            // Cooldown has elapsed: the first caller wins the compare-and-swap and is let through as a probe,
            // which clears the open deadline.
            Assert.True(SqlServerSearchService.TryEnterQueryStoreCircuit());
            Assert.Equal(0, SqlServerSearchService.GetQueryStoreCircuitOpenUntilTicksForTests());
        }

        [Fact]
        public void GivenProbeAfterCooldown_WhenProbeFails_ThenBreakerReopens()
        {
            SqlServerSearchService.SetQueryStoreCircuitStateForTests(
                SqlServerSearchService.QueryStoreCircuitBreakerFailureThreshold,
                DateTime.UtcNow.AddSeconds(-1).Ticks);

            // Probe is let through...
            Assert.True(SqlServerSearchService.TryEnterQueryStoreCircuit());

            // ...but the probe fails, which (already at/over threshold) re-opens the breaker for another cooldown.
            SqlServerSearchService.RecordQueryStoreFailure();

            Assert.False(SqlServerSearchService.TryEnterQueryStoreCircuit());
        }
    }
}
