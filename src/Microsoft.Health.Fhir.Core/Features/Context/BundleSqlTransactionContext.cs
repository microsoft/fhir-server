// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;

namespace Microsoft.Health.Fhir.Core.Features.Context
{
    /// <summary>
    /// Default ambient bundle SQL transaction context implementation.
    /// </summary>
    public sealed class BundleSqlTransactionContext : IBundleSqlTransactionContext
    {
        private readonly AsyncLocal<int> _scopeDepth = new();

        /// <summary>
        /// Gets a value indicating whether the current async flow is within a bundle SQL transaction scope.
        /// </summary>
        public bool IsActive => _scopeDepth.Value > 0;

        /// <summary>
        /// Enters a bundle SQL transaction context scope for the current async flow.
        /// </summary>
        /// <returns>A disposable handle that exits the scope when disposed.</returns>
        public IDisposable Enter()
        {
            _scopeDepth.Value++;
            return new Scope(_scopeDepth);
        }

        private sealed class Scope : IDisposable
        {
            private readonly AsyncLocal<int> _scopeDepth;
            private bool _disposed;

            public Scope(AsyncLocal<int> scopeDepth)
            {
                _scopeDepth = scopeDepth;
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                if (_scopeDepth.Value > 0)
                {
                    _scopeDepth.Value--;
                }
            }
        }
    }
}
