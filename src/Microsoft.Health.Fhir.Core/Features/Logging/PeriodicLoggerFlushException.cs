// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.Core.Features.Logging
{
    /// <summary>
    /// The exception that is thrown when the wrapped logger fails while a <see cref="PeriodicLogger{T}"/> emits a
    /// detached batch of aggregated information entries.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This exception identifies a failure that originated inside the wrapped logging provider. Failures that
    /// originate in the periodic timer or the flush loop itself are surfaced with their original exception type so
    /// that provider failures and infrastructure failures can be told apart.
    /// </para>
    /// <para>
    /// A detached batch is never retried, because the wrapped provider may already have accepted some entries before
    /// it failed and retrying the batch could duplicate them. The discarded counts describe the entries that were not
    /// confirmed as emitted, starting with the entry whose emission threw.
    /// </para>
    /// </remarks>
    public sealed class PeriodicLoggerFlushException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PeriodicLoggerFlushException"/> class.
        /// </summary>
        /// <param name="discardedEntryCount">
        /// The number of aggregated entries that were discarded, including the entry whose emission failed.
        /// </param>
        /// <param name="discardedOccurrenceCount">
        /// The total number of original information log occurrences represented by the discarded entries.
        /// </param>
        /// <param name="innerException">The exception thrown by the wrapped logger.</param>
        public PeriodicLoggerFlushException(int discardedEntryCount, long discardedOccurrenceCount, Exception innerException)
            : base(FormatMessage(discardedEntryCount, discardedOccurrenceCount), innerException)
        {
            DiscardedEntryCount = discardedEntryCount;
            DiscardedOccurrenceCount = discardedOccurrenceCount;
        }

        /// <summary>
        /// Gets the number of aggregated entries that were discarded, including the entry whose emission failed.
        /// </summary>
        public int DiscardedEntryCount { get; }

        /// <summary>
        /// Gets the total number of original information log occurrences represented by the discarded entries.
        /// </summary>
        public long DiscardedOccurrenceCount { get; }

        private static string FormatMessage(int discardedEntryCount, long discardedOccurrenceCount)
        {
            return FormattableString.Invariant(
                $"The wrapped logger failed while emitting aggregated information entries. {discardedEntryCount} aggregated entries representing {discardedOccurrenceCount} occurrences were discarded and are not retried.");
        }
    }
}
