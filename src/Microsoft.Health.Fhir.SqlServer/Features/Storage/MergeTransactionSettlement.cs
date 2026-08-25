// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Logging;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    /// <summary>
    /// Settles the merge transaction opened by dbo.MergeResourcesBeginTransaction when the merge never reaches
    /// dbo.MergeResources.
    /// </summary>
    /// <remarks>
    /// A merge takes a transaction id before it decides what to write. Normally dbo.MergeResources settles that
    /// transaction as part of writing (or the caller settles it explicitly when there is nothing to write), but every
    /// path that leaves the merge in between - a guarded version precondition that fails in a bundle transaction, a
    /// version probe that deadlocks, times out or loses its connection, a cancellation - leaves it open. An open
    /// transaction is not merely untidy: it blocks the visibility watermark that
    /// dbo.MergeResourcesAdvanceTransactionVisibility maintains until the transaction watchdog times it out, and a
    /// sequential transaction bundle can leak one per entry.
    /// <para>
    /// Once the merge has handed the transaction to dbo.MergeResources, that stored procedure and the existing
    /// failure handling around it own its fate - including the deliberate decision to leave a non single-transaction
    /// merge open so the transaction watchdog can roll it forward - so <see cref="TransferToMergeExecution"/> is
    /// called at that boundary and this type does nothing on disposal.
    /// </para>
    /// <para>
    /// Settlement is best effort and bounded. It always runs on its own connection and its own token so a caller
    /// that has already been cancelled still gets an attempt, but it never blocks the failing request beyond
    /// <see cref="SettlementTimeout"/> and never re-enters the SQL retry loop. Anything it cannot settle in that
    /// window is logged and left to the transaction watchdog, which is the durable backstop.
    /// </para>
    /// </remarks>
    internal sealed class MergeTransactionSettlement : IAsyncDisposable
    {
        private const string FailureReason = "Merge failed before any resource was sent to dbo.MergeResources.";

        /// <summary>
        /// Upper bound on the whole in-band settlement attempt - connection, execution, and any wait inside the
        /// store - after which the transaction is left to the transaction watchdog.
        /// </summary>
        /// <remarks>
        /// Settlement runs on a request that is already failing, so this is the extra latency that failure is
        /// allowed to cost. It is deliberately far below the store's configured command timeout and retry budget,
        /// which together can span minutes on a datastore blip; the watchdog is the durable backstop, so waiting
        /// longer here buys nothing except a slower failure. It is also never the SqlCommand default of 30 seconds,
        /// which ISqlRetryService would replace with the large store-wide timeout.
        /// </remarks>
        internal static readonly TimeSpan SettlementTimeout = TimeSpan.FromSeconds(5);

        private readonly SqlStoreClient _storeClient;
        private readonly ILogger _logger;
        private readonly long _transactionId;
        private readonly TimeSpan _settlementTimeout;
        private readonly int _settlementCommandTimeoutSeconds;
        private bool _transferred;

        public MergeTransactionSettlement(SqlStoreClient storeClient, ILogger logger, long transactionId)
            : this(storeClient, logger, transactionId, SettlementTimeout)
        {
        }

        internal MergeTransactionSettlement(SqlStoreClient storeClient, ILogger logger, long transactionId, TimeSpan settlementTimeout)
        {
            _storeClient = EnsureArg.IsNotNull(storeClient, nameof(storeClient));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
            _transactionId = transactionId;
            _settlementTimeout = EnsureArg.IsGt(settlementTimeout, TimeSpan.Zero, nameof(settlementTimeout));
            _settlementCommandTimeoutSeconds = Math.Max(1, (int)Math.Ceiling(settlementTimeout.TotalSeconds));
        }

        /// <summary>
        /// Records that the merge has reached the point where dbo.MergeResources and the failure handling around it
        /// decide whether the transaction is committed, marked failed, or deliberately left for the transaction
        /// watchdog to roll forward.
        /// </summary>
        public void TransferToMergeExecution()
        {
            _transferred = true;
        }

        public async ValueTask DisposeAsync()
        {
            if (_transferred)
            {
                return;
            }

            //// The token is deliberately not linked to the caller's. The most common way to reach disposal without
            //// transferring is that the caller's token was already cancelled, and settling under it would remove the
            //// one in-band chance to clean up. Its own bound - not the caller - is what keeps an already-failing
            //// request from waiting on a datastore that is not answering.
            using var settlementTimeout = new CancellationTokenSource(_settlementTimeout);

            try
            {
                //// Retries are disabled and the command timeout is pinned to the same bound, so a datastore blip
                //// cannot spend the store's retry count multiplied by its large command timeout here. The call still
                //// runs on its own connection and dbo.MergeResourcesCommitTransaction remains idempotent, so a
                //// settlement that lands late is harmless and a settlement that never lands is rolled forward by the
                //// transaction watchdog.
                await _storeClient.MergeResourcesCommitTransactionAsync(
                    _transactionId,
                    FailureReason,
                    settlementTimeout.Token,
                    commandTimeoutSeconds: _settlementCommandTimeoutSeconds,
                    disableRetries: true);
            }
            catch (Exception e)
            {
                // Best effort. The transaction watchdog is the backstop for a transaction that cannot be settled here,
                // and throwing from disposal would replace the failure that actually ended the merge. Both the "gave
                // up" and the "store refused" outcomes are reported the same way, because both leave exactly one
                // transaction for the watchdog to recover.
                _logger.LogWarning(
                    e,
                    "Unable to settle merge transaction {TransactionId} within its {SettlementTimeoutMs} ms bound ({SettlementOutcome}); leaving it to the transaction watchdog.",
                    _transactionId,
                    _settlementTimeout.TotalMilliseconds,
                    settlementTimeout.IsCancellationRequested ? "abandoned" : "failed");
            }
        }
    }
}
