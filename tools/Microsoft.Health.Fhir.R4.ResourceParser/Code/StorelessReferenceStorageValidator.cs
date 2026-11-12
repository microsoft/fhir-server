// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;

namespace Microsoft.Health.Fhir.R4.ResourceParser.Code
{
    /// <summary>
    /// This parser builds wrappers without a data store, so it has no column capacity to check.
    /// The server that later persists a wrapper applies its own store's capacity.
    /// </summary>
    public sealed class StorelessReferenceStorageValidator : IReferenceStorageValidator
    {
        public void EnsureRepresentable(ReferenceSearchValue value)
        {
            ArgumentNullException.ThrowIfNull(value);
        }
    }
}
