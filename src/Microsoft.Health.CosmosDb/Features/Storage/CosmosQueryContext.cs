// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Microsoft.Azure.Cosmos;

namespace Microsoft.Health.CosmosDb.Features.Storage
{
    /// <summary>
    /// Context used for executing a cosmos query.
    /// </summary>
    public class CosmosQueryContext : ICosmosQueryContext
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="CosmosQueryContext"/> class.
        /// </summary>
        /// <param name="sqlQuerySpec">The SQL query.</param>
        /// <param name="feedOptions">The options.</param>
        /// <param name="continuationToken">Continuation token</param>
        public CosmosQueryContext(QueryDefinition sqlQuerySpec, QueryRequestOptions feedOptions = null, string continuationToken = null)
        {
            EnsureArg.IsNotNull(sqlQuerySpec, nameof(sqlQuerySpec));

            SqlQuerySpec = sqlQuerySpec;
            FeedOptions = feedOptions;
            ContinuationToken = continuationToken;
        }

        /// <inheritdoc />
        public QueryDefinition SqlQuerySpec { get; }

        /// <inheritdoc />
        public QueryRequestOptions FeedOptions { get; }

        /// <inheritdoc />
        public string ContinuationToken { get; }
    }
}
