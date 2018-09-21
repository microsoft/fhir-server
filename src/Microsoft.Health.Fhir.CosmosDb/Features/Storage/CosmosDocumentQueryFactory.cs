// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Linq;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage
{
    /// <summary>
    /// Factory for creating the <see cref="CosmosDocumentQuery{T}"/>.
    /// </summary>
    public class CosmosDocumentQueryFactory : ICosmosDocumentQueryFactory
    {
        private readonly ICosmosDocumentQueryLogger _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="CosmosDocumentQueryFactory"/> class.
        /// </summary>
        /// <param name="logger">The logger.</param>
        public CosmosDocumentQueryFactory(ICosmosDocumentQueryLogger logger)
        {
            EnsureArg.IsNotNull(logger, nameof(logger));

            _logger = logger;
        }

        /// <inheritdoc />
        public IDocumentQuery<T> Create<T>(IDocumentClient documentClient, CosmosQueryContext context)
        {
            EnsureArg.IsNotNull(documentClient, nameof(documentClient));
            EnsureArg.IsNotNull(context, nameof(context));

            IDocumentQuery<T> documentQuery = documentClient.CreateDocumentQuery<T>(
                context.CollectionUri,
                context.SqlQuerySpec,
                context.FeedOptions)
                .AsDocumentQuery();

            return new CosmosDocumentQuery<T>(
                context,
                documentQuery,
                _logger);
        }
    }
}
