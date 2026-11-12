// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
namespace Microsoft.Health.Fhir.Core.Features.Search.SearchValues
{
    /// <summary>
    /// Decides whether the active data store can index or compare every component of a reference without loss.
    /// Each data store registers its own implementation; there is no permissive default.
    /// </summary>
    public interface IReferenceStorageValidator
    {
        /// <summary>
        /// Ensures the complete reference can be stored and compared without shortening or character substitution.
        /// </summary>
        /// <param name="value">The reference to check.</param>
        /// <exception cref="ReferenceStorageCapacityException">Thrown when a component of <paramref name="value"/> cannot be represented whole.</exception>
        void EnsureRepresentable(ReferenceSearchValue value);
    }
}
