// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Data.SqlClient;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.SqlServer.Features.Search;
using Microsoft.Health.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser
{
    public class ParserOptions
    {
        private const string MissingParameterManagerErrorMessage = "A SQL parameter manager is required to generate a search query.";

        public ContinuationToken? ContinuationToken { get; set; }

        public IncludesContinuationToken? IncludesContinuationToken { get; set; }

        public int CteNumber { get; set; } = 0;

        public string? LastCteName { get; set; }

        public int ChainLevel { get; set; } = 0;

        public bool IsLastInChainGroup { get; set; } = false;

        public bool ParentIsForwardChain { get; set; } = false;

        public bool Sort { get; set; }

        public int Count { get; set; } = 10;

        public int IncludeCount { get; set; } = 10;

        public IList<short> ResourceTypes { get; init; } = new List<short>();

        public IList<short> ExcludedResourceTypes { get; init; } = new List<short>();

        public bool GetTotalCount { get; set; }

        public string? SortParameterName { get; set; }

        public bool SortDescending { get; set; }

        public bool SortIsSpecialParameter { get; set; }

        public bool SortQuerySecondPhase { get; set; }

        public string SortContinuationToken { get; set; } = string.Empty;

        public long? SortContinuationResourceSurrogateId { get; set; }

        public bool IsIterateInclude { get; set; }

        /// <summary>
        /// Gets or sets the name of the result CTE produced by the parser.
        /// Set by chain/reverse-chain parsers so callers know which CTE to reference.
        /// </summary>
        public string? ResultCteName { get; set; }

        public ResourceVersionType ResourceVersionType { get; set; } = ResourceVersionType.Latest;

        public SqlQueryBuilder SqlQueryBuilder { get; set; } = new SqlQueryBuilder();

        /// <summary>
        /// Gets the request-scoped SQL parameter manager.
        /// </summary>
        public HashingSqlQueryParameterManager? ParameterManager { get; init; }

        /// <summary>
        /// Gets a value indicating whether the current request can reuse query plans.
        /// </summary>
        public bool ReuseQueryPlans { get; init; }

        /// <summary>
        /// Adds a typed SQL parameter for the provided column.
        /// </summary>
        /// <param name="column">The target SQL column.</param>
        /// <param name="value">The parameter value.</param>
        /// <param name="includeInHash">Whether the parameter should participate in query hashing.</param>
        /// <returns>The SQL parameter placeholder or literal value chosen by the parameter manager.</returns>
        /// <exception cref="InvalidOperationException">Thrown when no parameter manager is available.</exception>
        public object AddParameter(Column column, object value, bool includeInHash)
        {
            return GetRequiredParameterManager().AddParameter(column, value, includeInHash);
        }

        /// <summary>
        /// Adds a SQL parameter for the provided value.
        /// </summary>
        /// <param name="value">The parameter value.</param>
        /// <param name="includeInHash">Whether the parameter should participate in query hashing.</param>
        /// <returns>The SQL parameter.</returns>
        /// <exception cref="InvalidOperationException">Thrown when no parameter manager is available.</exception>
        public SqlParameter AddParameter(object value, bool includeInHash)
        {
            return GetRequiredParameterManager().AddParameter(value, includeInHash);
        }

        private HashingSqlQueryParameterManager GetRequiredParameterManager()
        {
            if (ParameterManager == null)
            {
                throw new InvalidOperationException(MissingParameterManagerErrorMessage);
            }

            return ParameterManager;
        }
    }
}
