// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading.Tasks;

namespace Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.CancelledDuringRetryDelay
{
    /// <summary>
    /// Orders the two collections in this scenario against each other.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The scenario needs one collection to cancel the run while the other is waiting to retry.
    /// Deciding when to cancel by sleeping makes that depend on how fast the machine is, so the
    /// cancelling collection waits to be told the retrying test has failed an attempt instead.
    /// </para>
    /// <para>
    /// The wait is asynchronous so that it does not hold one of the runner's bounded worker
    /// threads. A blocking wait here can leave the collection being waited for with no thread to
    /// run on, which turns the handshake into the stall it was meant to prevent.
    /// </para>
    /// </remarks>
    internal static class RetryDelayHandshake
    {
        /// <summary>
        /// Bounds the wait so that a scenario which has stopped interleaving fails with its own
        /// explanation, rather than hanging until the runner's timeout reports nothing useful.
        /// </summary>
        internal static readonly TimeSpan Budget = TimeSpan.FromSeconds(30);

        private static readonly TaskCompletionSource AttemptFailingSource =
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// Gets a task that completes as the retrying test fails an attempt, immediately before the
        /// retry delay this scenario needs the run to be cancelled during.
        /// </summary>
        internal static Task AttemptFailing => AttemptFailingSource.Task;

        /// <summary>
        /// Announces that the retrying test is failing an attempt.
        /// </summary>
        internal static void AnnounceAttemptFailing() => AttemptFailingSource.TrySetResult();
    }
}
