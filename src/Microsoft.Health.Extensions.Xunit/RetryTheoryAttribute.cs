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
        /// <summary>
        /// Initializes a new instance of the <see cref="RetryTheoryAttribute"/> class.
        /// </summary>
        /// <param name="sourceFilePath">The source file containing the test method. Supplied by the compiler.</param>
        /// <param name="sourceLineNumber">The line number of the test method. Supplied by the compiler.</param>
        public RetryTheoryAttribute([CallerFilePath] string sourceFilePath = "", [CallerLineNumber] int sourceLineNumber = 0)
            : base(sourceFilePath, sourceLineNumber)
        {
        }

        /// <summary>
        /// Gets or sets the maximum number of attempts, including the first run (default is 3).
        /// A value of 3 therefore runs a failing test three times in total, not four.
        /// </summary>
        public int MaxRetries { get; set; } = 3;

        /// <summary>
        /// Gets or sets the delay in milliseconds between retry attempts (default is 5000ms).
        /// </summary>
        public int DelayBetweenRetriesMs { get; set; } = 5000;

        /// <summary>
        /// Gets or sets whether to retry on assertion failures.
        /// Default is false - assertion failures usually indicate test bugs, not transient issues.
        /// Set to true for tests that validate eventually-consistent systems (e.g., cache refresh, reindex operations).
        /// <para>
        /// What counts as an assertion failure is decided from the reported exception's type name -
        /// one containing "Xunit" or "Assert", so that assertion libraries used alongside xUnit are
        /// recognised too - rather than from the exception type itself, which the message bus does
        /// not receive. Timeouts are deliberately excluded: they match that name test but are the
        /// canonical transient failure, so they are retried whatever this is set to.
        /// </para>
        /// </summary>
        public bool RetryOnAssertionFailure { get; set; } = false;
    }
}
