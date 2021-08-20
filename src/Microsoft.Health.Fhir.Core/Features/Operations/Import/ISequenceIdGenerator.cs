// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    /// <summary>
    /// Generator for sequence id.
    /// </summary>
    /// <typeparam name="T">Sequence id for type T.</typeparam>
    public interface ISequenceIdGenerator<T>
    {
        /// <summary>
        /// Get current sequence id.
        /// </summary>
        /// <returns>Sequence id for type T.</returns>
        T GetCurrentSequenceId();
    }
}
