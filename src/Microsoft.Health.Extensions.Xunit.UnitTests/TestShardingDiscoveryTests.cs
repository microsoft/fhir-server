// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Xunit;

namespace Microsoft.Health.Extensions.Xunit.UnitTests
{
    /// <summary>
    /// Exercises test sharding end to end through the real xUnit discoverer and the real test runner, by
    /// launching the sample test asset assembly as a child process with the sharding environment variables set.
    /// </summary>
    /// <remarks>
    /// A child process is used rather than in-process discovery for two reasons: the configuration is read from
    /// process environment variables, which must not be mutated in a test process that may run tests in parallel;
    /// and only a real run proves the runner's observable behavior, in particular that an invalid configuration
    /// produces a non-zero exit code rather than quietly discovering zero tests.
    /// </remarks>
    public class TestShardingDiscoveryTests
    {
        private const string ShardIndexVariable = TestShardConfiguration.ShardIndexEnvironmentVariableName;
        private const string ShardCountVariable = TestShardConfiguration.ShardCountEnvironmentVariableName;

        [Fact]
        public void GivenNoShardingConfiguration_WhenDiscovering_ThenAllTestsAreFound()
        {
            DiscoveryResult result = Discover(shardIndex: null, shardCount: null);

            Assert.Equal(0, result.ExitCode);
            Assert.NotEmpty(result.TestCases);

            // Parity guard: the unsharded run is what every existing pipeline does today.
            Assert.Equal(result.TestCases.Count, GetFullTestCaseSet().Count);
        }

        [Fact]
        public void GivenASingleShard_WhenDiscovering_ThenAllTestsAreFound()
        {
            DiscoveryResult result = DiscoverShard(shardIndex: 0, shardCount: 1);

            Assert.Equal(0, result.ExitCode);
            Assert.Equal(GetFullTestCaseSet(), result.TestCases.ToHashSet(StringComparer.Ordinal));
        }

        [Theory]
        [InlineData(2)]
        [InlineData(3)]
        public void GivenMultipleShards_WhenDiscovering_ThenTheShardsPartitionTheFullTestSet(int shardCount)
        {
            HashSet<string> expected = GetFullTestCaseSet();
            var shards = new List<HashSet<string>>();

            for (int shardIndex = 0; shardIndex < shardCount; shardIndex++)
            {
                DiscoveryResult result = DiscoverShard(shardIndex, shardCount);

                Assert.Equal(0, result.ExitCode);
                shards.Add(result.TestCases.ToHashSet(StringComparer.Ordinal));
            }

            // Exhaustive: no test case is lost.
            var union = new HashSet<string>(StringComparer.Ordinal);
            foreach (HashSet<string> shard in shards)
            {
                union.UnionWith(shard);
            }

            Assert.Equal(expected, union);

            // Disjoint: no test case runs twice.
            for (int i = 0; i < shards.Count; i++)
            {
                for (int j = i + 1; j < shards.Count; j++)
                {
                    Assert.Empty(shards[i].Intersect(shards[j], StringComparer.Ordinal));
                }
            }

            // Useful: sharding actually splits the work rather than assigning everything to one shard.
            Assert.All(shards, shard => Assert.NotEmpty(shard));
        }

        [Fact]
        public void GivenMultipleShards_WhenDiscovering_ThenAllFixtureVariantsAndTheoryRowsOfAMethodStayTogether()
        {
            const int shardCount = 3;
            var owningShardByMethod = new Dictionary<string, int>(StringComparer.Ordinal);

            for (int shardIndex = 0; shardIndex < shardCount; shardIndex++)
            {
                DiscoveryResult result = DiscoverShard(shardIndex, shardCount);
                Assert.Equal(0, result.ExitCode);

                foreach (string testCase in result.TestCases)
                {
                    string method = GetDeclaringTypeAndMethod(testCase);

                    if (owningShardByMethod.TryGetValue(method, out int existingShard))
                    {
                        Assert.True(
                            existingShard == shardIndex,
                            $"{method} was split across shards {existingShard} and {shardIndex}.");
                    }
                    else
                    {
                        owningShardByMethod[method] = shardIndex;
                    }
                }
            }

            // Sanity check that the asset assembly really does contain the shapes this test is guarding:
            // a method whose fixture variants differ from its class's, and methods with multiple theory rows.
            Assert.Contains("Microsoft.Health.Extensions.Xunit.TestAssets.VariantTests.OverridingMethod", owningShardByMethod.Keys);
            Assert.Contains("Microsoft.Health.Extensions.Xunit.TestAssets.PlainTests.PlainTheory", owningShardByMethod.Keys);
        }

        [Theory]
        [InlineData("0", null, "half-configured")]
        [InlineData(null, "2", "half-configured")]
        [InlineData("0", "0", "must be at least 1")]
        [InlineData("-1", "2", "must be at least 0")]
        [InlineData("2", "2", "must be at least 0")]
        [InlineData("notanumber", "2", "must be an integer")]
        [InlineData("0", "notanumber", "must be an integer")]
        public void GivenAnInvalidShardingConfiguration_WhenDiscovering_ThenTheRunFailsLoudly(string shardIndex, string shardCount, string expectedMessageFragment)
        {
            DiscoveryResult result = Discover(shardIndex, shardCount);

            // The whole point: an invalid configuration must not look like a successful run of zero tests.
            Assert.NotEqual(0, result.ExitCode);
            Assert.Empty(result.TestCases);
            Assert.Contains(nameof(TestShardConfigurationException), result.Output, StringComparison.Ordinal);
            Assert.Contains(expectedMessageFragment, result.Output, StringComparison.Ordinal);
        }

        private static HashSet<string> GetFullTestCaseSet()
        {
            DiscoveryResult result = Discover(shardIndex: null, shardCount: null);
            Assert.Equal(0, result.ExitCode);

            return result.TestCases.ToHashSet(StringComparer.Ordinal);
        }

        /// <summary>
        /// Reduces a discovered test case name to its declaring type and method, dropping the fixture argument
        /// decoration "(StoreA, FormatA)" and the theory row arguments "(value: 1)".
        /// </summary>
        /// <param name="testCaseName">The discovered test case display name.</param>
        /// <returns>The declaring type and method name.</returns>
        private static string GetDeclaringTypeAndMethod(string testCaseName)
        {
            string withoutTheoryArguments = testCaseName;

            int lastOpenParenthesis = withoutTheoryArguments.IndexOf('(', StringComparison.Ordinal);
            while (lastOpenParenthesis >= 0)
            {
                int closingParenthesis = withoutTheoryArguments.IndexOf(')', lastOpenParenthesis);
                if (closingParenthesis < 0)
                {
                    break;
                }

                withoutTheoryArguments = withoutTheoryArguments.Remove(lastOpenParenthesis, (closingParenthesis - lastOpenParenthesis) + 1);
                lastOpenParenthesis = withoutTheoryArguments.IndexOf('(', StringComparison.Ordinal);
            }

            return withoutTheoryArguments;
        }

        private static DiscoveryResult DiscoverShard(int shardIndex, int shardCount)
        {
            return Discover(
                shardIndex.ToString(System.Globalization.CultureInfo.InvariantCulture),
                shardCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        private static DiscoveryResult Discover(string shardIndex, string shardCount)
        {
            var startInfo = new ProcessStartInfo(GetTestAssetExecutablePath())
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = GetTestAssetDirectory(),
            };

            startInfo.ArgumentList.Add("--list-tests");

            // Set on the child only. The test process's own environment is never modified.
            startInfo.Environment.Remove(ShardIndexVariable);
            startInfo.Environment.Remove(ShardCountVariable);

            if (shardIndex != null)
            {
                startInfo.Environment[ShardIndexVariable] = shardIndex;
            }

            if (shardCount != null)
            {
                startInfo.Environment[ShardCountVariable] = shardCount;
            }

            var output = new StringBuilder();

            using Process process = Process.Start(startInfo);
            Assert.NotNull(process);

            process.OutputDataReceived += (_, e) => AppendLine(output, e.Data);
            process.ErrorDataReceived += (_, e) => AppendLine(output, e.Data);
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            Assert.True(
                process.WaitForExit(milliseconds: 120_000),
                "The test asset discovery process did not exit within the timeout.");

            // Ensures the asynchronous output handlers have drained.
            process.WaitForExit();

            string combinedOutput = output.ToString();

            return new DiscoveryResult(process.ExitCode, combinedOutput, ParseTestCases(combinedOutput));
        }

        private static void AppendLine(StringBuilder output, string line)
        {
            if (line == null)
            {
                return;
            }

            lock (output)
            {
                output.AppendLine(line);
            }
        }

        private static IReadOnlyList<string> ParseTestCases(string output)
        {
            const string testCasePrefix = "Microsoft.Health.Extensions.Xunit.TestAssets.";

            return output
                .Split('\n')
                .Select(line => line.Trim('\r', ' ', '\t'))
                .Where(line => line.StartsWith(testCasePrefix, StringComparison.Ordinal))
                .ToList();
        }

        private static string GetTestAssetDirectory()
        {
            string directory = typeof(TestShardingDiscoveryTests).Assembly
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .Single(a => a.Key == "TestAssetsDirectory")
                .Value;

            Assert.True(Directory.Exists(directory), $"The test asset output directory was not found: {directory}");

            return Path.GetFullPath(directory);
        }

        private static string GetTestAssetExecutablePath()
        {
            string fileName = "Microsoft.Health.Extensions.Xunit.TestAssets" + (OperatingSystem.IsWindows() ? ".exe" : string.Empty);
            string path = Path.Combine(GetTestAssetDirectory(), fileName);

            Assert.True(File.Exists(path), $"The test asset executable was not found: {path}");

            return path;
        }

        private sealed class DiscoveryResult
        {
            public DiscoveryResult(int exitCode, string output, IReadOnlyList<string> testCases)
            {
                ExitCode = exitCode;
                Output = output;
                TestCases = testCases;
            }

            public int ExitCode { get; }

            public string Output { get; }

            public IReadOnlyList<string> TestCases { get; }
        }
    }
}
