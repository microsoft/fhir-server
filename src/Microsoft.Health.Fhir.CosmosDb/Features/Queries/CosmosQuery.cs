// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Azure.Cosmos;
using Microsoft.Health.Abstractions.Exceptions;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Queries
{
    /// <summary>
    /// Wrapper on <see cref="CosmosQuery"/> to provide common error status code to exceptions handling.
    /// </summary>
    /// <typeparam name="T">The result type.</typeparam>
    public class CosmosQuery<T> : ICosmosQuery<T>
    {
        private readonly ICosmosQueryContext _queryContext;
        private readonly FeedIterator<T> _feedIterator;
        private readonly ICosmosResponseProcessor _processor;
        private readonly ICosmosQueryLogger _logger;

        private string _continuationToken;
        private bool _hasLoggedQuery;

        /// <summary>
        /// Initializes a new instance of the <see cref="CosmosQuery{T}"/> class.
        /// </summary>
        /// <param name="queryContext">The query context.</param>
        /// <param name="feedIterator">The feed iterator to enumerate.</param>
        /// <param name="processor">Response processor</param>
        /// <param name="logger">The logger.</param>
        public CosmosQuery(
            ICosmosQueryContext queryContext,
            FeedIterator<T> feedIterator,
            ICosmosResponseProcessor processor,
            ICosmosQueryLogger logger)
        {
            EnsureArg.IsNotNull(queryContext, nameof(queryContext));
            EnsureArg.IsNotNull(feedIterator, nameof(feedIterator));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _queryContext = queryContext;
            _feedIterator = feedIterator;
            _processor = processor;
            _logger = logger;

            _continuationToken = _queryContext.ContinuationToken;
        }

        /// <summary>
        /// Gets a value indicating whether there are more results.
        /// </summary>
        public bool HasMoreResults => _feedIterator.HasMoreResults;

        /// <inheritdoc />
        public async Task<FeedResponse<T>> ExecuteNextAsync(CancellationToken token = default)
        {
            if (!_hasLoggedQuery)
            {
                _logger.LogQueryExecution(
                    _queryContext.SqlQuerySpec,
                    _queryContext.FeedOptions?.PartitionKey?.ToString(),
                    _continuationToken,
                    _queryContext.FeedOptions?.MaxItemCount,
                    _queryContext.FeedOptions?.MaxConcurrency);
                _hasLoggedQuery = true;
            }

            try
            {
                FeedResponse<T> response = await _feedIterator.ReadNextAsync(token);

                _continuationToken = response.ContinuationToken;

                return response;
            }
            catch (CosmosException ex)
            {
                // The SDK wraps exceptions we throw in handlers with a CosmosException.
                Exception fhirException = ex.InnerException as FhirException ?? ex.InnerException as MicrosoftHealthException;

                if (fhirException == null)
                {
                    await _processor.ProcessErrorResponseAsync(ex.StatusCode, ex.Headers, ex.Message, token);
                }

                throw;
            }
        }
    }
}
