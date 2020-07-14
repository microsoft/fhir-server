// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Azure.Cosmos;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Queries
{
    /// <summary>
    /// Factory for creating the <see cref="CosmosQuery{T}"/>.
    /// </summary>
    public class CosmosQueryFactory : ICosmosQueryFactory
    {
        private readonly ICosmosQueryLogger _logger;
        private readonly ICosmosResponseProcessor _cosmosResponseProcessor;

        /// <summary>
        /// Initializes a new instance of the <see cref="CosmosQueryFactory"/> class.
        /// </summary>
        /// <param name="cosmosResponseProcessor">The cosmos response processor</param>
        /// <param name="logger">The logger.</param>
        public CosmosQueryFactory(ICosmosResponseProcessor cosmosResponseProcessor, ICosmosQueryLogger logger)
        {
            EnsureArg.IsNotNull(logger, nameof(logger));
            EnsureArg.IsNotNull(cosmosResponseProcessor, nameof(cosmosResponseProcessor));

            _cosmosResponseProcessor = cosmosResponseProcessor;
            _logger = logger;
        }

        /// <inheritdoc />
        public ICosmosQuery<T> Create<T>(Container container, CosmosQueryContext context)
        {
            EnsureArg.IsNotNull(container, nameof(container));
            EnsureArg.IsNotNull(context, nameof(context));

            var documentQuery = container
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
