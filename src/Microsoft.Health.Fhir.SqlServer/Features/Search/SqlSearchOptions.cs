// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
using Microsoft.Health.Fhir.Core.Features.Search;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search
{
    // Holds properties specific to Sql search.
    public class SqlSearchOptions : SearchOptions
    {
        public SqlSearchOptions(SearchOptions searchOptions)
            : base(searchOptions)
        {
        }

        public SqlSearchOptions(SqlSearchOptions sqlSearchOptions)
            : base(sqlSearchOptions)
        {
            SortQuerySecondPhase = sqlSearchOptions.SortQuerySecondPhase;
            DidWeSearchForSortValue = sqlSearchOptions.DidWeSearchForSortValue;
        }

        /// <summary>
        /// Marks whether we need to execute the second set of queries for (certain types of) sort.
        /// </summary>
        public bool SortQuerySecondPhase { get; internal set; } = false;

        /// <summary>
        /// Sets whether this search query is of type sort with filter.
        /// </summary>
        public bool IsSortWithFilter
        {
            get
            {
                if (Sort.Count == 0)
                {
                    return false;
                }

                return QueryParams.ContainsKey(Sort[0].searchParameterInfo.Code);
            }
        }

        /// <summary>
        /// Keeps track of whether we searched for sort values as part of the current SQL query.
        /// </summary>
        public bool? DidWeSearchForSortValue { get; internal set; }

        /// <summary>
        /// Keeps track of whether missing modifier is specified for search parameter used in sort.
        /// </summary>
        public bool SortHasMissingModifier
        {
            get
            {
                if (Sort.Count == 0)
                {
                    return false;
                }

                return QueryParams.ContainsKey(Sort[0].searchParameterInfo.Code + ":missing");
            }
        }

        /// <summary>
        /// Performs a shallow clone of this instance
        /// </summary>
        public SqlSearchOptions CloneSqlSearchOptions() => (SqlSearchOptions)MemberwiseClone();
    }
}
