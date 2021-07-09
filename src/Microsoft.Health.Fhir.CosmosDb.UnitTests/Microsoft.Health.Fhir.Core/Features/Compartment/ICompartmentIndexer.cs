// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;

namespace Microsoft.Health.Fhir.Core.Features.Compartment
{
    public interface ICompartmentIndexer
    {
        /// <summary>
        /// Extracts the compartment index entries.
        /// </summary>
        /// <param name="resourceType">The resourceType to extract the compartment indices from.</param>
        /// <param name="searchIndices">The search indices for the resource.</param>
        /// <returns>ComaprtmentIndices.</returns>
        CompartmentIndices Extract(string resourceType, IReadOnlyCollection<SearchIndexEntry> searchIndices);
    }
}
