// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Linq;

namespace Microsoft.Health.CosmosDb.Features.Storage
{
    /// <summary>
    /// Factory for creating the <see cref="IDocumentQuery{T}"/>.
    /// </summary>
    public class CosmosDocumentQueryFactory : ICosmosDocumentQueryFactory
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="CosmosDocumentQueryFactory"/> class.
        /// </summary>
        public CosmosDocumentQueryFactory()
        {
        }

        /// <inheritdoc />
        public IDocumentQuery<T> Create<T>(IDocumentClient documentClient, CosmosQueryContext context)
        {
            EnsureArg.IsNotNull(documentClient, nameof(documentClient));
            EnsureArg.IsNotNull(context, nameof(context));

            return documentClient.CreateDocumentQuery<T>(
                context.CollectionUri,
                context.SqlQuerySpec,
                context.FeedOptions)
                .AsDocumentQuery();
        }
    }
}
