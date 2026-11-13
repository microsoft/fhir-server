// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Persistence
{
    public sealed class MergeOptions
    {
        /// <summary>
        /// Merge options for SQL operations.
        /// </summary>
        /// <remarks>
        /// A few combinations for merge options:
        ///     Standalone POST/PUT/DELETE/PATCH operations:
        ///     - enlistTransaction = false, isBundleTransaction = false
        ///     Import:
        ///     - enlistTransaction = false, isBundleTransaction = false
        ///     Sequential batches:
        ///     - enlistTransaction = false, isBundleTransaction = false
        ///     Sequential transactions (rely on C# transactions):
        ///     - enlistTransaction = true, isBundleTransaction = true
        ///     Parallel batches:
        ///     - enlistTransaction = false, isBundleTransaction = false
        ///     Parallel transactions (rely on SQL transactions):
        ///     - enlistTransaction = false, isBundleTransaction = true
        /// </remarks>
        public MergeOptions(bool enlistTransaction = false, bool isBundleTransaction = false)
        {
            EnlistInTransaction = enlistTransaction;
            IsBundleTransaction = isBundleTransaction;
        }

        public static MergeOptions Default { get; private set; } = new MergeOptions();

        /// <summary>
        /// Indicates whether to enlist in a C# transaction.
        /// This should only be set to true for sequential transaction bundles, as they rely on C# transactions.
        /// Standalone operations and parallel bundle transactions should not enlist transactions, as they should rely on SQL transactions.
        /// </summary>
        public bool EnlistInTransaction { get; private set; }

        public bool IsBundleTransaction { get; private set;  }
    }
}
