﻿// -------------------------------------------------------------------------------------------------
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
        private int _maxItemCount;
        private int _includeCount;

        /// <summary>
        /// Initializes a new instance of the <see cref="SearchOptions"/> class.
        /// It hides constructor and prevent object creation not through <see cref="ISearchOptionsFactory"/>
        /// </summary>
        internal SearchOptions()
        {
        }

        internal SearchOptions(SearchOptions other)
        {
            ContinuationToken = other.ContinuationToken;
            CountOnly = other.CountOnly;
            IncludeTotal = other.IncludeTotal;
            OnlyIds = other.OnlyIds;

            MaxItemCountSpecifiedByClient = other.MaxItemCountSpecifiedByClient;
            Expression = other.Expression;
            UnsupportedSearchParams = new List<Tuple<string, string>>(other.UnsupportedSearchParams);
            Sort = new List<(SearchParameterInfo, SortOrder)>(other.Sort);

            if (other.MaxItemCount > 0)
            {
                MaxItemCount = other.MaxItemCount;
            }

            if (other.IncludeCount > 0)
            {
                IncludeCount = other.IncludeCount;
            }

            QueryHints = other.QueryHints;

            ResourceVersionTypes = other.ResourceVersionTypes;
        }

        /// <summary>
        /// Gets the optional continuation token.
        /// </summary>
        public string ContinuationToken { get; internal set; }

        /// <summary>
        /// Gets the optional feed range used by CosmosDb queries.
        /// </summary>
        public string FeedRange { get; internal set; }

        /// <summary>
        /// Gets a value indicating whether to only return the record count
        /// </summary>
        public bool CountOnly { get; internal set; }

        /// <summary>
        /// This is used for sql force reindex where we need to ignore the Resource SearchParamHash field when searching
        /// </summary>
        public bool IgnoreSearchParamHash { get; set; }

        /// <summary>
        /// Indicates if the total number of resources that match the search parameters should be calculated.
        /// </summary>
        /// <remarks>The ability to retrieve an estimate of the total is yet to be implemented.</remarks>
        public TotalType IncludeTotal { get; internal set; }

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

                _maxItemCount = value;
            }
        }

        /// <summary>
        /// Indicates whether MaxItemCount was explicitly set by the client.
        /// </summary>
        public bool MaxItemCountSpecifiedByClient { get; internal set; }

        /// <summary>
        /// Get the number of items to include in search results.
        /// </summary>
        public int IncludeCount
        {
            get => _includeCount;
            internal set
            {
                if (value <= 0)
                {
                    throw new InvalidOperationException(Core.Resources.InvalidSearchCountSpecified);
                }

                _includeCount = value;
            }
        }

        /// <summary>
        /// Which version types (latest, soft-deleted, history) to include in search.
        /// </summary>
        public ResourceVersionType ResourceVersionTypes { get; internal set; } = ResourceVersionType.Latest;

        /// <summary>
        /// Gets the search expression.
        /// </summary>
        public Expression Expression { get; internal set; }

        /// <summary>
        /// Gets the list of search parameters that were not used in the search.
        /// </summary>
        public IReadOnlyList<Tuple<string, string>> UnsupportedSearchParams { get; internal set; }

        /// <summary>
        /// Gets the list of sorting parameters.
        /// </summary>
        public IReadOnlyList<(SearchParameterInfo searchParameterInfo, SortOrder sortOrder)> Sort { get; internal set; }

        public IReadOnlyList<(string Param, string Value)> QueryHints { get; set; }

        public bool OnlyIds { get; set; }

        /// <summary>
        /// Flag for async operations that want to return a large number of results.
        /// </summary>
        public bool IsLargeAsyncOperation { get; internal set; }

        /// <summary>
        /// Performs a shallow clone of this instance
        /// </summary>
        public SearchOptions Clone() => (SearchOptions)MemberwiseClone();
    }
}
