// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.BoundedParallelism
{
    /// <summary>
    /// The body every class in this scenario runs, so that each class contributes one test that
    /// overlaps with the others for long enough to be observed.
    /// </summary>
    /// <remarks>
    /// Each class is its own collection, and collections are the unit of parallelization, so running
    /// this scenario with a thread limit of one must serialize them. Asserting on the count taken as
    /// a test enters, rather than on a maximum read at the end, keeps the check free of races: if the
    /// limit is not applied the classes start together and all but one see a count above the limit.
    /// </remarks>
    public abstract class BoundedParallelismTestBase
    {
        /// <summary>
        /// Occupies the thread limit for long enough that any other class allowed to start would be
        /// seen doing so.
        /// </summary>
        /// <returns>A task that completes when the body has finished.</returns>
        protected static async Task RunBody()
        {
            int concurrent = ConcurrencyTracker.Enter();

            try
            {
                Assert.True(
                    concurrent <= 1,
                    $"{concurrent} tests ran at once under a thread limit of one, so collection parallelism is not being bounded.");

                await Task.Delay(TimeSpan.FromMilliseconds(300));
            }
            finally
            {
                ConcurrencyTracker.Exit();
            }
        }
    }
}
