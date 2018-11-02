// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;

namespace Microsoft.Health.Fhir.Core.Features.Search
{
    /// <summary>
    /// Represents a search index entry.
    /// </summary>
    public class SearchIndexEntry
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SearchIndexEntry"/> class.
        /// </summary>
        /// <param name="searchParameter">The search parameter</param>
        /// <param name="value">The searchable value.</param>
        public SearchIndexEntry(SearchParameter searchParameter, ISearchValue value)
        {
            EnsureArg.IsNotNull(value, nameof(value));
            SearchParameter = searchParameter;
            Value = value;
        }

        public SearchParameter SearchParameter { get; }

        /// <summary>
        /// Gets the searchable value.
        /// </summary>
        public ISearchValue Value { get; }
    }
}
