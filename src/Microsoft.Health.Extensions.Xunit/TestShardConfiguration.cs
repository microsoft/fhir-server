// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Globalization;
using System.Text;
using EnsureThat;

namespace Microsoft.Health.Extensions.Xunit
{
    /// <summary>
    /// Describes which shard of a test assembly the current process is responsible for running.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The partition is computed from the declaring type's full name and the method name only. It therefore
    /// keeps every theory row and every fixture argument set variant of a method in the same shard, and it is
    /// decided before theory rows are enumerated. That makes it independent of theory pre-enumeration settings,
    /// of test case unique ID formats, and of which runner is hosting the assembly.
    /// </para>
    /// <para>
    /// The partition is a pure function of those two strings, so shard assignment is identical in every process,
    /// on every machine, and in every FHIR version's copy of a shared test class. The assignment is stable as
    /// long as the type and method names are stable; renaming a test method may move it to a different shard,
    /// which is harmless because shards are always run together.
    /// </para>
    /// </remarks>
    public sealed class TestShardConfiguration
    {
        /// <summary>
        /// The environment variable holding the zero-based index of the shard the current process should run.
        /// </summary>
        public const string ShardIndexEnvironmentVariableName = "MicrosoftHealthTestShardIndex";

        /// <summary>
        /// The environment variable holding the total number of shards the test assembly is split into.
        /// </summary>
        public const string ShardCountEnvironmentVariableName = "MicrosoftHealthTestShardCount";

        private const ulong FnvOffsetBasis = 14695981039346656037;
        private const ulong FnvPrime = 1099511628211;

        private TestShardConfiguration(int shardIndex, int shardCount)
        {
            ShardIndex = shardIndex;
            ShardCount = shardCount;
        }

        /// <summary>
        /// Gets a configuration that runs every test method, which is the behavior when sharding is not configured.
        /// </summary>
        public static TestShardConfiguration Disabled { get; } = new TestShardConfiguration(0, 1);

        /// <summary>
        /// Gets the zero-based index of the shard this process runs.
        /// </summary>
        public int ShardIndex { get; }

        /// <summary>
        /// Gets the total number of shards the assembly is partitioned into.
        /// </summary>
        public int ShardCount { get; }

        /// <summary>
        /// Gets a value indicating whether this configuration actually excludes any test methods.
        /// </summary>
        public bool IsSharding => ShardCount > 1;

        /// <summary>
        /// Creates a configuration from raw environment variable values.
        /// </summary>
        /// <param name="shardIndexValue">The raw value of the shard index variable, or null/empty when unset.</param>
        /// <param name="shardCountValue">The raw value of the shard count variable, or null/empty when unset.</param>
        /// <returns>The parsed configuration, or <see cref="Disabled"/> when both values are unset.</returns>
        /// <exception cref="TestShardConfigurationException">The values do not describe a valid shard.</exception>
        public static TestShardConfiguration Create(string shardIndexValue, string shardCountValue)
        {
            bool hasIndex = !string.IsNullOrWhiteSpace(shardIndexValue);
            bool hasCount = !string.IsNullOrWhiteSpace(shardCountValue);

            if (!hasIndex && !hasCount)
            {
                return Disabled;
            }

            if (!hasIndex || !hasCount)
            {
                throw new TestShardConfigurationException(
                    FormattableString.Invariant(
                        $"Test sharding is half-configured. '{ShardIndexEnvironmentVariableName}' and '{ShardCountEnvironmentVariableName}' must both be set or both be unset. Actual: {ShardIndexEnvironmentVariableName}=[{shardIndexValue}], {ShardCountEnvironmentVariableName}=[{shardCountValue}]."));
            }

            int shardIndex = ParseInt32(shardIndexValue, ShardIndexEnvironmentVariableName);
            int shardCount = ParseInt32(shardCountValue, ShardCountEnvironmentVariableName);

            if (shardCount < 1)
            {
                throw new TestShardConfigurationException(
                    FormattableString.Invariant(
                        $"'{ShardCountEnvironmentVariableName}' must be at least 1. Actual: [{shardCount}]."));
            }

            if (shardIndex < 0 || shardIndex >= shardCount)
            {
                throw new TestShardConfigurationException(
                    FormattableString.Invariant(
                        $"'{ShardIndexEnvironmentVariableName}' must be at least 0 and less than '{ShardCountEnvironmentVariableName}'. Actual: {ShardIndexEnvironmentVariableName}=[{shardIndex}], {ShardCountEnvironmentVariableName}=[{shardCount}]."));
            }

            return new TestShardConfiguration(shardIndex, shardCount);
        }

        /// <summary>
        /// Creates a configuration by reading the sharding environment variables.
        /// </summary>
        /// <param name="environmentVariableReader">Reads an environment variable by name.</param>
        /// <returns>The parsed configuration, or <see cref="Disabled"/> when the variables are unset.</returns>
        /// <exception cref="TestShardConfigurationException">The variables do not describe a valid shard.</exception>
        public static TestShardConfiguration FromEnvironment(Func<string, string> environmentVariableReader)
        {
            EnsureArg.IsNotNull(environmentVariableReader, nameof(environmentVariableReader));

            return Create(
                environmentVariableReader(ShardIndexEnvironmentVariableName),
                environmentVariableReader(ShardCountEnvironmentVariableName));
        }

        /// <summary>
        /// Determines whether the test method identified by the supplied names belongs to this shard.
        /// </summary>
        /// <param name="declaringTypeFullName">The full name of the type declaring the test method, without fixture argument decoration.</param>
        /// <param name="methodName">The name of the test method.</param>
        /// <returns><c>true</c> when this shard is responsible for the method.</returns>
        public bool Includes(string declaringTypeFullName, string methodName)
        {
            EnsureArg.IsNotNullOrEmpty(declaringTypeFullName, nameof(declaringTypeFullName));
            EnsureArg.IsNotNullOrEmpty(methodName, nameof(methodName));

            if (!IsSharding)
            {
                return true;
            }

            return GetShard(declaringTypeFullName, methodName, ShardCount) == ShardIndex;
        }

        /// <summary>
        /// Computes the shard index a test method is assigned to.
        /// </summary>
        /// <param name="declaringTypeFullName">The full name of the type declaring the test method.</param>
        /// <param name="methodName">The name of the test method.</param>
        /// <param name="shardCount">The total number of shards.</param>
        /// <returns>The zero-based shard index.</returns>
        public static int GetShard(string declaringTypeFullName, string methodName, int shardCount)
        {
            EnsureArg.IsNotNullOrEmpty(declaringTypeFullName, nameof(declaringTypeFullName));
            EnsureArg.IsNotNullOrEmpty(methodName, nameof(methodName));
            EnsureArg.IsGte(shardCount, 1, nameof(shardCount));

            // FNV-1a (64 bit). string.GetHashCode is deliberately not used: it is randomized per process in
            // .NET Core, which would give different shards different views of the same assembly.
            ulong hash = FnvOffsetBasis;
            hash = AddToHash(hash, declaringTypeFullName);
            hash = AddToHash(hash, ".");
            hash = AddToHash(hash, methodName);

            return (int)(hash % (ulong)shardCount);
        }

        private static ulong AddToHash(ulong hash, string value)
        {
            foreach (byte b in Encoding.UTF8.GetBytes(value))
            {
                hash ^= b;
                hash *= FnvPrime;
            }

            return hash;
        }

        private static int ParseInt32(string value, string environmentVariableName)
        {
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
            {
                throw new TestShardConfigurationException(
                    FormattableString.Invariant($"'{environmentVariableName}' must be an integer. Actual: [{value}]."));
            }

            return parsed;
        }
    }
}
