// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Linq;
using Microsoft.Health.CosmosDb.Features.Storage;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage
{
    /// <summary>
    /// Factory for creating the <see cref="FhirDocumentQuery{T}"/>.
    /// </summary>
    public class FhirCosmosDocumentQueryFactory : ICosmosDocumentQueryFactory
    {
        private readonly IFhirDocumentQueryLogger _logger;
        private readonly ICosmosResponseProcessor _cosmosResponseProcessor;

        /// <summary>
        /// Initializes a new instance of the <see cref="FhirCosmosDocumentQueryFactory"/> class.
        /// </summary>
        /// <param name="cosmosResponseProcessor">The cosmos response processor</param>
        /// <param name="logger">The logger.</param>
        public FhirCosmosDocumentQueryFactory(ICosmosResponseProcessor cosmosResponseProcessor, IFhirDocumentQueryLogger logger)
        {
            EnsureArg.IsNotNull(logger, nameof(logger));
            EnsureArg.IsNotNull(cosmosResponseProcessor, nameof(cosmosResponseProcessor));

            _cosmosResponseProcessor = cosmosResponseProcessor;
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

            return new FhirDocumentQuery<T>(
                context,
                documentQuery,
                _cosmosResponseProcessor,
                _logger);
        }
    }
}
