// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
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
        private readonly Func<IDocumentClient> _documentClientFactory;
        private readonly ICosmosDocumentQueryLogger _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="CosmosDocumentQueryFactory"/> class.
        /// </summary>
        /// <param name="documentClientFactory">
        /// A function that returns an <see cref="IDocumentClient"/>.
        /// Note that this is a function so that the lifetime of the instance is not directly controlled by the IoC container.
        /// </param>
        /// <param name="logger">The logger.</param>
        public CosmosDocumentQueryFactory(Func<IDocumentClient> documentClientFactory, ICosmosDocumentQueryLogger logger)
        {
            EnsureArg.IsNotNull(documentClientFactory, nameof(documentClientFactory));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _documentClientFactory = documentClientFactory;

            _logger = logger;
        }

        /// <inheritdoc />
        public IDocumentQuery<T> Create<T>(CosmosQueryContext context)
        {
            EnsureArg.IsNotNull(context, nameof(context));

            var documentClient = _documentClientFactory.Invoke();

            if (documentClient == null)
            {
                throw new InvalidOperationException(
                    string.Format(Core.Resources.MethodReturnedNull, nameof(Func<IDocumentClient>)));
            }

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
