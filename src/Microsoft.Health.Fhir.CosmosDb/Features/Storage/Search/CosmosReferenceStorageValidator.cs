// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage.Search
{
    /// <summary>
    /// Cosmos DB stores the reference base and ID as complete JSON strings (see <see cref="SearchIndexEntryJObjectGenerator"/>),
    /// so it compares them whole and has no column capacity. Identity grammar and length are enforced earlier by
    /// <see cref="Microsoft.Health.Fhir.Core.Features.Validation.FhirPrimitiveTypes.ResourceIdPolicy"/>.
    /// </summary>
    internal sealed class CosmosReferenceStorageValidator : IReferenceStorageValidator
    {
        public void EnsureRepresentable(ReferenceSearchValue value)
        {
            EnsureArg.IsNotNull(value, nameof(value));
        }
    }
}
