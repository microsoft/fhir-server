// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Azure.Cosmos;
using Microsoft.Health.CosmosDb.Features.Storage;

namespace Microsoft.Health.CosmosDb.Features.Queries
{
    /// <summary>
    /// Wrapper on <see cref="CosmosQuery"/> to provide common error status code to exceptions handling.
    /// </summary>
    /// <typeparam name="T">The result type.</typeparam>
    public class CosmosQuery<T> : ICosmosQuery<T>
    {
        private readonly ICosmosQueryContext _queryContext;
        private readonly FeedIterator<T> _documentQuery;
        private readonly ICosmosResponseProcessor _cosmosResponseProcessor;
        private readonly IDocumentQueryLogger _logger;

        private string _continuationToken;

        /// <summary>
        /// Initializes a new instance of the <see cref="CosmosQuery{T}"/> class.
        /// </summary>
        /// <param name="queryContext">The query context.</param>
        /// <param name="documentQuery">The document query to execute.</param>
        /// <param name="cosmosResponseProcessor">The cosmos response processor.</param>
        /// <param name="logger">The logger.</param>
        public CosmosQuery(
            ICosmosQueryContext queryContext,
            FeedIterator<T> documentQuery,
            ICosmosResponseProcessor cosmosResponseProcessor,
            IDocumentQueryLogger logger)
        {
            EnsureArg.IsNotNull(queryContext, nameof(queryContext));
            EnsureArg.IsNotNull(documentQuery, nameof(documentQuery));
            EnsureArg.IsNotNull(cosmosResponseProcessor, nameof(cosmosResponseProcessor));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _queryContext = queryContext;
            _documentQuery = documentQuery;
            _cosmosResponseProcessor = cosmosResponseProcessor;
            _logger = logger;

            _continuationToken = _queryContext.ContinuationToken;
        }

        /// <summary>
        /// Gets a value indicating whether there are more results.
        /// </summary>
        public bool HasMoreResults => _documentQuery.HasMoreResults;

        /// <inheritdoc />
        public async Task<FeedResponse<T>> ExecuteNextAsync(CancellationToken token = default)
        {
            Guid queryId = Guid.NewGuid();

            _logger.LogQueryExecution(
                queryId,
                _queryContext.SqlQuerySpec,
                _continuationToken,
                _queryContext.FeedOptions?.MaxItemCount);

            try
            {
                FeedResponse<T> response = await _documentQuery.ReadNextAsync(token);

                _continuationToken = response.ContinuationToken;

                _logger.LogQueryExecutionResult(
                    queryId,
                    response.ActivityId,
                    response.RequestCharge,
                    response.ContinuationToken,
                    response.ETag,
                    response.Count);

                return response;
            }
            catch (CosmosException ex)
            {
                _logger.LogQueryExecutionResult(
                    queryId,
                    ex.ActivityId,
                    ex.RequestCharge,
                    null,
                    null,
                    0,
                    ex);

                throw;
            }
        }
    }
}
