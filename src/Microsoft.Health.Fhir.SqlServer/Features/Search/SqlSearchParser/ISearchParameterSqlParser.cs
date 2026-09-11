// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Search;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser
{
    /// <summary>
    /// Parses FHIR search parameters into a SQL search query.
    /// </summary>
    public interface ISearchParameterSqlParser
    {
        /// <summary>
        /// Parses multiple FHIR search parameters into a SQL search query.
        /// </summary>
        /// <param name="parameters">The search parameter values keyed by parameter name.</param>
        /// <param name="sqlSearchOptions">The SQL search options for the request.</param>
        /// <param name="parameterManager">The manager that adds values to the SQL command.</param>
        /// <param name="reuseQueryPlans">Whether generated SQL should support query plan reuse.</param>
        /// <param name="continuationToken">The optional search continuation token.</param>
        /// <param name="includesContinuationToken">The optional include continuation token.</param>
        /// <returns>The generated SQL query, or <see langword="null"/> when no query is generated.</returns>
        string? ParseMultiple(
            IDictionary<string, IList<string>> parameters,
            SqlSearchOptions sqlSearchOptions,
            HashingSqlQueryParameterManager parameterManager,
            bool reuseQueryPlans,
            ContinuationToken? continuationToken = null,
            IncludesContinuationToken? includesContinuationToken = null);
    }
}
