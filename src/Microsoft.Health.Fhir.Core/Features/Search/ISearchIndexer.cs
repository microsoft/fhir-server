// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Hl7.Fhir.ElementModel;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search
{
    /// <summary>
    /// Provides a mechanism to extract search index entries.
    /// </summary>
    public interface ISearchIndexer
    {
        /// <summary>
        /// Extracts the search index entries.
        /// </summary>
        /// <param name="resource">The resource to extract the search indices from.</param>
        /// <returns>An <see cref="IEnumerable{SearchIndex}"/> that contains the search index entries.</returns>
        IReadOnlyCollection<SearchIndexEntry> Extract(ResourceElement resource);
    }
}
