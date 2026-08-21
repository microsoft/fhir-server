// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Extensions.Xunit
{
    /// <summary>
    /// How a retry attempt that published no result of its own should be accounted for.
    /// </summary>
    /// <remarks>
    /// An attempt reports nothing when the run is cancelled or aborted underneath it. That is not
    /// the same as passing, and the choice made here decides whether an already observed failure
    /// still reaches the results.
    /// </remarks>
    internal enum NoResultOutcome
    {
        /// <summary>
        /// No attempt ever published a result, so the test contributes nothing to the totals.
        /// </summary>
        ReportNothing,

        /// <summary>
        /// This attempt observed a failure before it was cut short, so that failure is replayed.
        /// </summary>
        ReplayCurrentAttempt,

        /// <summary>
        /// An earlier attempt deferred a failure while waiting to be retried, and the retry never
        /// reported anything, so the earlier failure is replayed.
        /// </summary>
        ReplayEarlierAttempt,
    }
}
