// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.SqlServer.Features.Search.QueryPlanCache;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search.QueryPlanCache
{
    /// <summary>
    /// Unit tests for <see cref="Ewma"/>.
    /// </summary>
    public class EwmaTests
    {
        [Fact]
        public void Ewma_Constructor_InitializesScoresToNull()
        {
            // Arrange
            var metrics = new[] { "A", "B", "C" };

            // Act
            var ewma = new Ewma(metrics, minIterationsForDecision: 2);

            // Assert
            foreach (var metric in metrics)
            {
                // Reflection to access private field for test
                var scores = typeof(Ewma)
                    .GetField("_ewmaScores", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                    .GetValue(ewma) as System.Collections.Concurrent.ConcurrentDictionary<string, double?>;

                Assert.True(scores.ContainsKey(metric));
                Assert.Null(scores[metric]);
            }
        }

        [Fact]
        public void Ewma_Update_UpdatesScoreCorrectly()
        {
            // Arrange
            var metrics = new[] { "A" };
            var ewma = new Ewma(metrics, minIterationsForDecision: 0, alpha: 0.5);

            // Act
            ewma.Update("A", 10.0); // First update, should set score to 10
            ewma.Update("A", 20.0); // Second update, should be 0.5*20 + 0.5*10 = 15

            // Assert
            var scores = typeof(Ewma)
                .GetField("_ewmaScores", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .GetValue(ewma) as System.Collections.Concurrent.ConcurrentDictionary<string, double?>;

            Assert.Equal(15.0, scores["A"]);
        }

        [Fact]
        public void Ewma_Update_ThrowsForUnknownMetric()
        {
            // Arrange
            var ewma = new Ewma(new[] { "A" }, minIterationsForDecision: 0);

            // Act & Assert
            Assert.Throws<ArgumentException>(() => ewma.Update("B", 1.0));
        }

        [Fact]
        public void Ewma_GetBestMetric_RoundRobinBeforeMinIterations()
        {
            // Arrange
            var metrics = new[] { "A", "B" };
            var ewma = new Ewma(metrics, minIterationsForDecision: 4);

            // Act & Assert
            Assert.Equal("A", ewma.GetBestMetric());
            Assert.Equal("B", ewma.GetBestMetric());
            Assert.Equal("A", ewma.GetBestMetric());
            Assert.Equal("B", ewma.GetBestMetric());
        }

        [Fact]
        public void Ewma_GetBestMetric_ReturnsLowestScoreAfterMinIterations()
        {
            // Arrange
            var metrics = new[] { "A", "B" };
            var ewma = new Ewma(metrics, minIterationsForDecision: 2);

            // Prime the scores
            ewma.Update("A", 5.0);
            ewma.Update("B", 3.0);

            // Simulate minIterations reached
            ewma.GetBestMetric();
            ewma.GetBestMetric();

            // Act
            var best = ewma.GetBestMetric();

            // Assert
            Assert.Equal("B", best);
        }

        [Fact]
        public void Ewma_IterationsProperty_IsAccurate()
        {
            // Arrange
            var metrics = new[] { "A", "B" };
            var ewma = new Ewma(metrics, minIterationsForDecision: 2);

            // Act
            ewma.GetBestMetric();
            ewma.GetBestMetric();
            ewma.GetBestMetric();

            // Assert
            Assert.Equal(3, ewma.Iterations);
        }
    }
}
