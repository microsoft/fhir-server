// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Linq;
using Microsoft.Health.CosmosDb.Features.Storage;
using Microsoft.Health.Fhir.CosmosDb.Features.Metrics;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage
{
    /// <summary>
    /// Factory for creating the <see cref="FhirDocumentQuery{T}"/>.
    /// </summary>
    public class FhirCosmosDocumentQueryFactory : ICosmosDocumentQueryFactory
    {
        private readonly IFhirDocumentQueryLogger _logger;
        private readonly ICosmosMetricProcessor _cosmosMetricProcessor;
        private readonly ICosmosExceptionProcessor _cosmosExceptionProcessor;

        /// <summary>
        /// Initializes a new instance of the <see cref="FhirCosmosDocumentQueryFactory"/> class.
        /// </summary>
        /// <param name="cosmosMetricProcessor">The metric processor</param>
        /// <param name="cosmosExceptionProcessor">The exception processor</param>
        /// <param name="logger">The logger.</param>
        public FhirCosmosDocumentQueryFactory(ICosmosMetricProcessor cosmosMetricProcessor, ICosmosExceptionProcessor cosmosExceptionProcessor, IFhirDocumentQueryLogger logger)
        {
            EnsureArg.IsNotNull(logger, nameof(logger));
            EnsureArg.IsNotNull(cosmosMetricProcessor, nameof(cosmosMetricProcessor));
            EnsureArg.IsNotNull(cosmosExceptionProcessor, nameof(cosmosExceptionProcessor));

            _cosmosMetricProcessor = cosmosMetricProcessor;
            _cosmosExceptionProcessor = cosmosExceptionProcessor;
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
                _cosmosMetricProcessor,
                _cosmosExceptionProcessor,
                _logger);
        }
    }
}
