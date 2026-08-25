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
    /// </remarks>
    internal sealed class MergeTransactionSettlement : IAsyncDisposable
    {
        private const string FailureReason = "Merge failed before any resource was sent to dbo.MergeResources.";

        private readonly SqlStoreClient _storeClient;
        private readonly ILogger _logger;
        private readonly long _transactionId;
        private bool _transferred;

        public MergeTransactionSettlement(SqlStoreClient storeClient, ILogger logger, long transactionId)
        {
            _storeClient = EnsureArg.IsNotNull(storeClient, nameof(storeClient));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
            _transactionId = transactionId;
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

            try
            {
                //// CancellationToken.None: this settles a transaction that is already known to be unusable, and the
                //// most common way to get here without transferring is that the caller's token was cancelled.
                await _storeClient.MergeResourcesCommitTransactionAsync(_transactionId, FailureReason, CancellationToken.None);
            }
            catch (Exception e)
            {
                // Best effort. The transaction watchdog is the backstop for a transaction that cannot be settled here,
                // and throwing from disposal would replace the failure that actually ended the merge.
                _logger.LogWarning(e, "Unable to settle merge transaction {TransactionId}; leaving it to the transaction watchdog.", _transactionId);
            }
        }
    }
}
