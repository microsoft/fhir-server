// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Security.Cryptography;
using Microsoft.Health.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search
{
    // Holds properties specific to Sql search.
    public class SqlSearchOptions : SearchOptions
    {
        public SqlSearchOptions(SearchOptions searchOptions)
            : base(searchOptions)
        {
        }

        /// <summary>
        /// Marks whether we need to execute the second set of queries for (certain types of) sort.
        /// </summary>
        public bool SortQuerySecondPhase { get; internal set; } = false;

        /// <summary>
        /// Sets whether this search query is of type sort with filter.
        /// </summary>
        public bool IsSortWithFilter { get; internal set; } = false;

        /// <summary>
        /// Keeps track of whether we searched for sort values as part of the current SQL query.
        /// </summary>
        public bool? DidWeSearchForSortValue { get; internal set; }

        /// <summary>
        /// Keeps track of whether missing modifier is specified for search parameter used in sort.
        /// </summary>
        public bool SortHasMissingModifier { get; internal set; }

        /// <summary>
        /// Performs a shallow clone of this instance
        /// </summary>
        public SqlSearchOptions CloneSqlSearchOptions() => (SqlSearchOptions)MemberwiseClone();

        public string GetHash()
        {
            // I don't think UnsupportedSearchParams is needed as it isn't used in the actual search
            var expressionHash = default(HashCode);
            Expression.AddValueInsensitiveHashCode(ref expressionHash);

            var sort = Sort?.Aggregate(string.Empty, (string result, (SearchParameterInfo param, SortOrder order) input) =>
            {
                return result + $"{input.param.Url}_{input.order}_";
            });

            var queryHints = QueryHints?.Aggregate(string.Empty, (string result, (string param, string value) input) =>
            {
                return result + $"{input.param}_{input.value}_";
            });

            var hashString = $"{ContinuationToken}_{FeedRange}_{CountOnly}_{IgnoreSearchParamHash}_{IncludeTotal}_{MaxItemCount}_{MaxItemCountSpecifiedByClient}_{IncludeCount}_{ResourceVersionTypes}_{OnlyIds}_{IsLargeAsyncOperation}_{SortQuerySecondPhase}_{IsSortWithFilter}_{DidWeSearchForSortValue}_{SortHasMissingModifier}_{expressionHash.ToHashCode()}_{sort}_{queryHints}";

            return hashString.ComputeHash();
        }
    }
}
