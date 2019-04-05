// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Models;

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
        public SearchIndexEntry(SearchParameterInfo searchParameter, ISearchValue value)
        {
            EnsureArg.IsNotNull(searchParameter, nameof(searchParameter));
            EnsureArg.IsNotNull(value, nameof(value));

            SearchParameter = searchParameter;
            Value = value;
        }

        /// <summary>
        /// Gets the search parameter
        /// </summary>
        public SearchParameterInfo SearchParameter { get; }

        /// <summary>
        /// Gets the searchable value.
        /// </summary>
        public ISearchValue Value { get; }
    }
}
