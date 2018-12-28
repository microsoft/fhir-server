// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;

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
        /// <param name="collectionUri">The collection URI.</param>
        /// <param name="sqlQuerySpec">The SQL query.</param>
        /// <param name="feedOptions">The options.</param>
        public CosmosQueryContext(Uri collectionUri, SqlQuerySpec sqlQuerySpec, FeedOptions feedOptions = null)
        {
            EnsureArg.IsNotNull(collectionUri, nameof(collectionUri));
            EnsureArg.IsNotNull(sqlQuerySpec, nameof(sqlQuerySpec));

            CollectionUri = collectionUri;
            SqlQuerySpec = sqlQuerySpec;
            FeedOptions = feedOptions;
        }

        /// <inheritdoc />
        public Uri CollectionUri { get; }

        /// <inheritdoc />
        public SqlQuerySpec SqlQuerySpec { get; }

        /// <inheritdoc />
        public FeedOptions FeedOptions { get; }
    }
}
