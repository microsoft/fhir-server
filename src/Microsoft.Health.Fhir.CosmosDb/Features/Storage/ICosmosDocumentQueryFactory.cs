// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Azure.Documents.Linq;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage
{
    /// <summary>
    /// Factory for creating the <see cref="CosmosDocumentQuery{T}"/>.
    /// </summary>
    public interface ICosmosDocumentQueryFactory
    {
        /// <summary>
        /// Creates an instance of <see cref="CosmosDocumentQuery{T}"/>.
        /// </summary>
        /// <typeparam name="T">The document type.</typeparam>
        /// <param name="queryContext">The SQL query context.</param>
        /// <returns>An instance of <see cref="CosmosDocumentQuery{T}"/>.</returns>
        IDocumentQuery<T> Create<T>(CosmosQueryContext queryContext);
    }
}
