// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.Core.Features.Context
{
    /// <summary>
    /// Provides ambient bundle SQL transaction context for the current async flow.
    /// </summary>
    public interface IBundleSqlTransactionContext
    {
        /// <summary>
        /// Gets a value indicating whether the current async flow is within a bundle SQL transaction scope.
        /// </summary>
        bool IsActive { get; }

        /// <summary>
        /// Enters a bundle SQL transaction context scope for the current async flow.
        /// </summary>
        /// <returns>A disposable handle that exits the scope when disposed.</returns>
        IDisposable Enter();
    }
}
