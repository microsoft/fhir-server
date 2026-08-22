// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.CancelledDuringPassingAttempt
{
    /// <summary>
    /// Orders the two collections in this scenario against each other.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The scenario needs one collection to cancel the run while the other is part way through a
    /// retry attempt. Arranging that with sleeps makes the outcome depend on how fast the machine
    /// is: on a loaded agent the cancelling collection's failure can land after the other
    /// collection's window has closed, and the scenario then fails with a message about itself
    /// rather than reporting anything about the behaviour under test. Waiting for an announcement
    /// orders the two by construction instead.
    /// </para>
    /// <para>
    /// Both waits here are asynchronous, which is not a matter of taste. The runner executes
    /// collections on a bounded set of worker threads, so a collection that blocks a thread while
    /// waiting for its sibling can leave the sibling with no thread to run on. That was measured:
    /// with a blocking wait the collection being waited for did not start until after the waiter
    /// had already given up, which is the same failure this handshake exists to remove.
    /// </para>
    /// </remarks>
    internal static class PassingAttemptHandshake
    {
        /// <summary>
        /// Bounds every wait so that a scenario which has stopped interleaving fails with its own
        /// explanation, rather than hanging until the runner's timeout reports nothing useful.
        /// </summary>
        internal static readonly TimeSpan Budget = TimeSpan.FromSeconds(30);

        private static readonly TaskCompletionSource SecondAttemptRunningSource =
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// Gets a task that completes once the retrying test's second attempt is running, which is
        /// the point from which cancelling the run exercises what this scenario exists to check.
        /// </summary>
        internal static Task SecondAttemptRunning => SecondAttemptRunningSource.Task;

        /// <summary>
        /// Announces that the retrying test's second attempt is running.
        /// </summary>
        internal static void AnnounceSecondAttempt() => SecondAttemptRunningSource.TrySetResult();

        /// <summary>
        /// Waits for the run to be cancelled, giving up after <see cref="Budget"/>.
        /// </summary>
        /// <param name="cancellationToken">The run's cancellation token.</param>
        /// <returns><c>true</c> if the run was cancelled; <c>false</c> if the budget expired first.</returns>
        internal static async Task<bool> WaitForCancellationAsync(CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(Budget, cancellationToken);
                return false;
            }
            catch (OperationCanceledException)
            {
                return true;
            }
        }
    }
}
