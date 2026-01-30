// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Xunit;

namespace Microsoft.Health.Extensions.Xunit
{
    /// <summary>
    /// Attribute that marks a test method to be retried a specified number of times if it fails.
    /// Useful for handling transient failures in integration and end-to-end tests.
    /// </summary>
    /// <remarks>
    /// With Microsoft.Testing.Platform and xUnit v3, retries are configured globally via command line (--retry-failed-tests N)
    /// or via the Microsoft.Testing.Extensions.Retry package. This attribute is kept for backward compatibility
    /// and as documentation of which tests are expected to be flaky.
    /// The attributes serve as markers for tests that may need retries, and the actual retry
    /// behavior is handled by the Microsoft.Testing.Platform infrastructure.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class RetryFactAttribute : FactAttribute
    {
        /// <summary>
        /// Gets or sets the maximum number of retry attempts (default is 3).
        /// Note: This property is for documentation/configuration purposes. Actual retries
        /// are handled by Microsoft.Testing.Platform's retry extension.
        /// </summary>
        public int MaxRetries { get; set; } = 3;

        /// <summary>
        /// Gets or sets the delay in milliseconds between retry attempts (default is 5000ms).
        /// Note: This property is for documentation/configuration purposes. Actual retries
        /// are handled by Microsoft.Testing.Platform's retry extension.
        /// </summary>
        public int DelayBetweenRetriesMs { get; set; } = 5000;

        /// <summary>
        /// Gets or sets whether to retry on assertion failures (XunitException).
        /// Default is false - assertion failures usually indicate test bugs, not transient issues.
        /// Set to true for tests that validate eventually-consistent systems (e.g., cache refresh, reindex operations).
        /// Note: This property is for documentation/configuration purposes. Actual retries
        /// are handled by Microsoft.Testing.Platform's retry extension.
        /// </summary>
        public bool RetryOnAssertionFailure { get; set; } = false;
    }
}
