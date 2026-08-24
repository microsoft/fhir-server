// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.RetryTimeouts
{
    /// <summary>
    /// A timeout is the canonical flaky failure, so a retry harness has to treat it as retriable.
    /// Expected: 2 results, 1 passed and 1 failed.
    /// </summary>
    public class RetryTimeoutTests
    {
        private static int _flakyTimeoutAttempts;

        /// <summary>
        /// A test that exceeds its timeout on the first attempt and completes on the second must be
        /// reported as passed. xUnit reports a timeout as <c>Xunit.Sdk.TestTimeoutException</c>,
        /// whose name contains "Xunit", so a retry decision made by matching that substring would
        /// take it for a deterministic assertion failure and spend none of the remaining attempts.
        /// </summary>
        [RetryFact(MaxRetries = 3, DelayBetweenRetriesMs = 10, Timeout = 500)]
        public async Task FlakyTimeout_IsRetriedAndPasses()
        {
            if (Interlocked.Increment(ref _flakyTimeoutAttempts) == 1)
            {
                await Task.Delay(5000);
            }
        }

        /// <summary>
        /// A test that exceeds its timeout on every attempt must still be reported as failed once,
        /// rather than losing the final attempt's failure the way a deferred failure would.
        /// </summary>
        [RetryFact(MaxRetries = 2, DelayBetweenRetriesMs = 10, Timeout = 500)]
        public async Task AlwaysTimesOut_IsReportedFailedOnce()
        {
            await Task.Delay(5000);
        }
    }
}
