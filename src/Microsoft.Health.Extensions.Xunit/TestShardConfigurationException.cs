// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Extensions.Xunit
{
    /// <summary>
    /// Thrown when the test sharding environment variables are present but do not describe a valid shard.
    /// </summary>
    /// <remarks>
    /// This is deliberately thrown while the test framework is creating its discoverer, so that an invalid
    /// configuration fails the test run loudly instead of silently discovering zero tests.
    /// </remarks>
    public sealed class TestShardConfigurationException : Exception
    {
        public TestShardConfigurationException(string message)
            : base(message)
        {
        }

        public TestShardConfigurationException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        public TestShardConfigurationException()
        {
        }
    }
}
