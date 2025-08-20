// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Xunit;
using Xunit.Sdk;

namespace Microsoft.Health.Extensions.Xunit
{
    /// <summary>
    /// Attribute that retries a parameterized test method on failure.
    /// Use this instead of [Theory] for flaky parameterized tests that need retry capability.
    /// Note: This attribute is currently not implemented. Use RetryFact for simple test retry functionality.
    /// </summary>
    public sealed class RetryTheoryAttribute : TheoryAttribute
    {
        /// <summary>
        /// Gets or sets the maximum number of retry attempts.
        /// Default is 3.
        /// </summary>
        public int MaxRetries { get; set; } = 3;

        /// <summary>
        /// Gets or sets the delay between retry attempts in milliseconds.
        /// Default is 1000ms (1 second).
        /// </summary>
        public int DelayMs { get; set; } = 1000;

        /// <summary>
        /// Gets or sets the timeout for each test attempt in milliseconds.
        /// If a test attempt takes longer than this, it will be cancelled and retried.
        /// Default is 0 (no timeout).
        /// </summary>
        public int TimeoutMs { get; set; } = 0;

        /// <summary>
        /// Gets or sets whether to use exponential backoff for retry delays.
        /// When true, delays will be DelayMs, DelayMs*2, DelayMs*4, etc.
        /// Default is false.
        /// </summary>
        public bool UseExponentialBackoff { get; set; } = false;

        /// <summary>
        /// Gets or sets the types of exceptions that should trigger a retry.
        /// If not specified, common transient exceptions will be retried.
        /// </summary>
        public Type[] RetryableExceptionTypes { get; set; }
    }
}
