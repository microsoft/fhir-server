// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Microsoft.Health.Extensions.Xunit.UnitTests
{
    /// <summary>
    /// Tests the shard partition function and configuration parsing in isolation. These never read or write
    /// process environment variables; the environment is only exercised through child processes in
    /// <see cref="TestShardingDiscoveryTests"/>.
    /// </summary>
    public class TestShardConfigurationTests
    {
        [Fact]
        public void GivenNoEnvironmentValues_WhenCreating_ThenShardingIsDisabled()
        {
            TestShardConfiguration configuration = TestShardConfiguration.Create(null, null);

            Assert.False(configuration.IsSharding);
            Assert.True(configuration.Includes("Some.Type", "SomeMethod"));
        }

        [Theory]
        [InlineData("", "")]
        [InlineData("   ", "  ")]
        public void GivenBlankEnvironmentValues_WhenCreating_ThenShardingIsDisabled(string index, string count)
        {
            Assert.False(TestShardConfiguration.Create(index, count).IsSharding);
        }

        [Theory]
        [InlineData("0", null)]
        [InlineData(null, "2")]
        [InlineData("0", "")]
        [InlineData("", "2")]
        public void GivenHalfConfiguredEnvironmentValues_WhenCreating_ThenItThrows(string index, string count)
        {
            TestShardConfigurationException exception = Assert.Throws<TestShardConfigurationException>(
                () => TestShardConfiguration.Create(index, count));

            Assert.Contains("half-configured", exception.Message, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("0", "0")]
        [InlineData("0", "-1")]
        [InlineData("-1", "2")]
        [InlineData("2", "2")]
        [InlineData("3", "2")]
        [InlineData("x", "2")]
        [InlineData("0", "y")]
        [InlineData("1.5", "2")]
        public void GivenInvalidEnvironmentValues_WhenCreating_ThenItThrows(string index, string count)
        {
            Assert.Throws<TestShardConfigurationException>(() => TestShardConfiguration.Create(index, count));
        }

        [Fact]
        public void GivenASingleShard_WhenCreating_ThenEveryMethodIsIncluded()
        {
            TestShardConfiguration configuration = TestShardConfiguration.Create("0", "1");

            Assert.False(configuration.IsSharding);
            Assert.All(GetSampleMethods(), m => Assert.True(configuration.Includes(m.TypeName, m.MethodName)));
        }

        [Fact]
        public void GivenAnEnvironmentReader_WhenCreatingFromEnvironment_ThenTheDocumentedVariableNamesAreRead()
        {
            var reads = new List<string>();

            TestShardConfiguration configuration = TestShardConfiguration.FromEnvironment(name =>
            {
                reads.Add(name);
                return name == TestShardConfiguration.ShardIndexEnvironmentVariableName ? "1" : "3";
            });

            Assert.Equal(
                new[] { "MicrosoftHealthTestShardIndex", "MicrosoftHealthTestShardCount" },
                reads);
            Assert.Equal(1, configuration.ShardIndex);
            Assert.Equal(3, configuration.ShardCount);
        }

        [Theory]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(7)]
        public void GivenAnyShardCount_WhenPartitioning_ThenEveryMethodLandsInExactlyOneShard(int shardCount)
        {
            var configurations = Enumerable.Range(0, shardCount)
                .Select(i => TestShardConfiguration.Create(i.ToString(), shardCount.ToString()))
                .ToList();

            foreach ((string typeName, string methodName) in GetSampleMethods())
            {
                int owners = configurations.Count(c => c.Includes(typeName, methodName));

                Assert.True(owners == 1, $"{typeName}.{methodName} was claimed by {owners} of {shardCount} shards.");
            }
        }

        [Fact]
        public void GivenTheSameMethod_WhenPartitioning_ThenTheShardIsStableAcrossCalls()
        {
            // The shard must not depend on anything process-scoped, most importantly not on string.GetHashCode,
            // which is randomized per process in .NET Core.
            foreach ((string typeName, string methodName) in GetSampleMethods())
            {
                int first = TestShardConfiguration.GetShard(typeName, methodName, 4);
                int second = TestShardConfiguration.GetShard(typeName, methodName, 4);

                Assert.Equal(first, second);
            }
        }

        [Fact]
        public void GivenKnownMethodNames_WhenPartitioning_ThenTheShardMatchesTheRecordedValues()
        {
            // Pins the hash so an accidental change to the algorithm is caught. The specific values do not matter,
            // only that they cannot change silently: shard assignment must be identical in every shard process.
            Assert.Equal(0, TestShardConfiguration.GetShard("Namespace.Type", "MethodA", 2));
            Assert.Equal(1, TestShardConfiguration.GetShard("Namespace.Type", "MethodB", 2));
            Assert.Equal(0, TestShardConfiguration.GetShard("Namespace.Type", "MethodA", 3));
            Assert.Equal(1, TestShardConfiguration.GetShard("Namespace.OtherType", "MethodA", 3));
        }

        [Fact]
        public void GivenDistinctMethods_WhenPartitioningIntoTwoShards_ThenBothShardsReceiveWork()
        {
            var shards = GetSampleMethods()
                .Select(m => TestShardConfiguration.GetShard(m.TypeName, m.MethodName, 2))
                .Distinct()
                .ToList();

            Assert.Equal(2, shards.Count);
        }

        [Fact]
        public void GivenAMissingTypeOrMethodName_WhenPartitioning_ThenItThrows()
        {
            Assert.ThrowsAny<ArgumentException>(() => TestShardConfiguration.GetShard(null, "M", 2));
            Assert.ThrowsAny<ArgumentException>(() => TestShardConfiguration.GetShard("T", string.Empty, 2));
            Assert.ThrowsAny<ArgumentException>(() => TestShardConfiguration.GetShard("T", "M", 0));
        }

        private static IReadOnlyList<(string TypeName, string MethodName)> GetSampleMethods()
        {
            return Enumerable.Range(0, 40)
                .Select(i => ($"Microsoft.Health.Sample.Type{i % 5}", $"TestMethod{i}"))
                .ToList();
        }
    }
}
