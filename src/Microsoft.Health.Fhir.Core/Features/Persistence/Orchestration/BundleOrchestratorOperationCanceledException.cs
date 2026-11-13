// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Persistence.Orchestration
{
    /// <summary>
    /// Thrown by <see cref="BundleOrchestratorOperation.SetStatusSafe"/> when a non-terminal status
    /// transition is attempted on an operation that is already in a terminal state (Failed or Canceled).
    /// Callers such as <see cref="BundleOrchestratorOperation.AppendResourceAsync"/> and
    /// <see cref="BundleOrchestratorOperation.ReleaseResourceAsync"/> catch this to silently
    /// short-circuit rather than propagating cascading error-level log noise.
    /// </summary>
    public sealed class BundleOrchestratorOperationCanceledException : BundleOrchestratorException
    {
        public BundleOrchestratorOperationCanceledException(string message)
            : base(message)
        {
        }
    }
}
