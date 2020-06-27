// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Azure.Cosmos;
using Microsoft.Health.Fhir.CosmosDb.Features.Queries;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage
{
    /// <summary>
    /// Factory for creating the <see cref="IDocumentQuery{T}"/>.
    /// </summary>
    public interface ICosmosQueryFactory
    {
        /// <summary>
        /// Creates an instance of <see cref="IDocumentQuery{T}"/>.
        /// </summary>
        /// <typeparam name="T">The document type.</typeparam>
        /// <param name="container">The document client</param>
        /// <param name="queryContext">The SQL query context.</param>
        /// <returns>An instance of <see cref="IDocumentQuery{T}"/>.</returns>
        ICosmosQuery<T> Create<T>(Container container, CosmosQueryContext queryContext);
    }
}
