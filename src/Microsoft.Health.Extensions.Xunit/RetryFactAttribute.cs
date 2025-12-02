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
    /// Attribute that marks a test method to be retried a specified number of times if it fails.
    /// Useful for handling transient failures in integration and end-to-end tests.
    /// </summary>
    [XunitTestCaseDiscoverer("Microsoft.Health.Extensions.Xunit.RetryFactDiscoverer", "Microsoft.Health.Extensions.Xunit")]
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class RetryFactAttribute : FactAttribute
    {
        /// <summary>
        /// Gets or sets the maximum number of retry attempts (default is 3).
        /// </summary>
        public int MaxRetries { get; set; } = 3;

        /// <summary>
        /// Gets or sets the delay in milliseconds between retry attempts (default is 5000ms).
        /// </summary>
        public int DelayBetweenRetriesMs { get; set; } = 5000;

        /// <summary>
        /// Gets or sets whether to retry on assertion failures (XunitException).
        /// Default is false - assertion failures usually indicate test bugs, not transient issues.
        /// Set to true for tests that validate eventually-consistent systems (e.g., cache refresh, reindex operations).
        /// </summary>
        public bool RetryOnAssertionFailure { get; set; } = false;
    }
}
