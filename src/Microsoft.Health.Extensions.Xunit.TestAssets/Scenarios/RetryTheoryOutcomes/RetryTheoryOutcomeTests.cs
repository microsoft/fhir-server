// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;
using Xunit;

namespace Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.RetryTheoryOutcomes
{
    /// <summary>
    /// The <see cref="RetryTheoryAttribute"/> outcome matrix. Expected: 3 results, one per data row,
    /// with the rows that recover reported as passed and the row that never recovers reported as
    /// failed.
    /// </summary>
    /// <remarks>
    /// Each row keeps its own attempt count, which only works if every row is discovered as its own
    /// test case and wrapped individually. A discoverer that collapsed the rows would either share
    /// the counter or lose rows outright, and both show up in the published results.
    /// </remarks>
    public class RetryTheoryOutcomeTests
    {
        private static readonly ConcurrentDictionary<int, int> Attempts = new ConcurrentDictionary<int, int>();

        /// <summary>
        /// Rows that recover on a later attempt must be reported as passed exactly once, and the row
        /// that fails every attempt must be reported as failed exactly once.
        /// </summary>
        /// <param name="row">Identifies the row so each keeps a separate attempt count.</param>
        /// <param name="recovers">Whether this row should stop failing after its first attempt.</param>
        [RetryTheory(MaxRetries = 3, DelayBetweenRetriesMs = 10, RetryOnAssertionFailure = true)]
        [InlineData(1, true)]
        [InlineData(2, false)]
        [InlineData(3, true)]
        public void FlakyRow(int row, bool recovers)
        {
            int attempt = Attempts.AddOrUpdate(row, 1, (_, current) => current + 1);

            if (!recovers || attempt == 1)
            {
                Assert.Fail($"ASSET: row {row} failing on attempt {attempt}");
            }
        }
    }
}
