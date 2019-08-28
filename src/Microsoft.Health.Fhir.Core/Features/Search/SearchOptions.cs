// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search
{
    /// <summary>
    /// Represents the search options.
    /// </summary>
    public class SearchOptions
    {
        private const int DefaultItemCountPerSearch = 10;
        private const int MaxItemCountPerSearch = 100;

        private int _maxItemCount = DefaultItemCountPerSearch;

        /// <summary>
        /// Gets the optional continuation token.
        /// </summary>
        public string ContinuationToken { get; internal set; }

        /// <summary>
        /// Gets a value indicating whether to only return the record count
        /// </summary>
        public bool CountOnly { get; internal set; }

        /// <summary>
        /// Gets the maximum number of items to find.
        /// </summary>
        public int MaxItemCount
        {
            get => _maxItemCount;

            internal set
            {
                if (value <= 0)
                {
                    throw new InvalidOperationException(Core.Resources.InvalidSearchCountSpecified);
                }

                // The server is allowed to return less than what client has asked (http://hl7.org/fhir/STU3/search.html#count).
                // Limit the maximum number of items if the client is asking too many.
                _maxItemCount = Math.Min(value, MaxItemCountPerSearch);
            }
        }

        /// <summary>
        /// Gets the search expression.
        /// </summary>
        public Expression Expression { get; internal set; }

        /// <summary>
        /// Gets the list of search parameters that were not used in the search.
        /// </summary>
        public IReadOnlyList<Tuple<string, string>> UnsupportedSearchParams { get; internal set; }

        /// <summary>
        /// Gets the list of unsupported sorting search parameters that were ignored in the search.
        /// </summary>
        public IReadOnlyList<(string parameterName, string reason)> UnsupportedSortingParams { get; internal set; }

        /// <summary>
        /// Gets the list of sorting parameters.
        /// </summary>
        public IReadOnlyList<(SearchParameterInfo searchParameterInfo, SortOrder sortOrder)> Sort { get; internal set; }
    }
}
