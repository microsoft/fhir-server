// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Threading
{
    /// <summary>
    /// Defines the types of operations for threading optimization.
    /// </summary>
    public enum OperationType
    {
        /// <summary>
        /// Export operations (primarily I/O bound).
        /// </summary>
        Export,

        /// <summary>
        /// Import operations (mixed I/O and CPU bound).
        /// </summary>
        Import,

        /// <summary>
        /// Bulk update operations (mixed I/O and CPU bound).
        /// </summary>
        BulkUpdate,

        /// <summary>
        /// Index rebuild operations (CPU intensive).
        /// </summary>
        IndexRebuild,

        /// <summary>
        /// Bundle processing operations.
        /// </summary>
        BundleProcessing,

        /// <summary>
        /// Search operations.
        /// </summary>
        Search,

        /// <summary>
        /// General operations.
        /// </summary>
        General,
    }
}
