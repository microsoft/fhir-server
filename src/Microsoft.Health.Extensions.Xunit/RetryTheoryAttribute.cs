// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Runtime.CompilerServices;
using Xunit;
using Xunit.v3;

namespace Microsoft.Health.Extensions.Xunit
{
    /// <summary>
    /// Attribute that marks a theory method to be retried a specified number of times if it fails.
    /// Useful for handling transient failures in integration and end-to-end tests with parameterized data.
    /// </summary>
    [XunitTestCaseDiscoverer(typeof(RetryTheoryDiscoverer))]
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class RetryTheoryAttribute : TheoryAttribute
    {
        public RetryTheoryAttribute([CallerFilePath] string sourceFilePath = "", [CallerLineNumber] int sourceLineNumber = 0)
        {
            SourceFilePath = sourceFilePath;
            SourceLineNumber = sourceLineNumber;
        }

        internal new string SourceFilePath { get; }

        internal new int SourceLineNumber { get; }

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
