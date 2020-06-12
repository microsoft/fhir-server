// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Azure.Cosmos;
using Microsoft.Health.CosmosDb.Features.Storage;

namespace Microsoft.Health.CosmosDb.Features.Queries
{
    /// <summary>
    /// Factory for creating the <see cref="CosmosQuery{T}"/>.
    /// </summary>
    public class CosmosQueryFactory : ICosmosQueryFactory
    {
        private readonly IDocumentQueryLogger _logger;
        private readonly ICosmosResponseProcessor _cosmosResponseProcessor;

        /// <summary>
        /// Initializes a new instance of the <see cref="CosmosQueryFactory"/> class.
        /// </summary>
        /// <param name="cosmosResponseProcessor">The cosmos response processor</param>
        /// <param name="logger">The logger.</param>
        public CosmosQueryFactory(ICosmosResponseProcessor cosmosResponseProcessor, IDocumentQueryLogger logger)
        {
            EnsureArg.IsNotNull(logger, nameof(logger));
            EnsureArg.IsNotNull(cosmosResponseProcessor, nameof(cosmosResponseProcessor));

            _cosmosResponseProcessor = cosmosResponseProcessor;
            _logger = logger;
        }

        /// <inheritdoc />
        public ICosmosQuery<T> Create<T>(Container documentClient, CosmosQueryContext context)
        {
            EnsureArg.IsNotNull(documentClient, nameof(documentClient));
            EnsureArg.IsNotNull(context, nameof(context));

            var documentQuery = documentClient
                .GetItemQueryIterator<T>(
                    context.SqlQuerySpec,
                    continuationToken: context.ContinuationToken,
                    requestOptions: context.FeedOptions);

            return new CosmosQuery<T>(
                context,
                documentQuery,
                _cosmosResponseProcessor,
                _logger);
        }
    }
}
