// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.ComponentModel;
using Xunit;
using Xunit.Sdk;

namespace Microsoft.Health.Extensions.Xunit
{
    /// <summary>
    /// Attribute that retries a test method on failure.
    /// Use this instead of [Fact] for flaky tests that need retry capability.
    /// </summary>
    [XunitTestCaseDiscoverer("Microsoft.Health.Extensions.Xunit.SimpleRetryFactDiscoverer", "Microsoft.Health.Extensions.Xunit")]
    public sealed class RetryFactAttribute : FactAttribute
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
