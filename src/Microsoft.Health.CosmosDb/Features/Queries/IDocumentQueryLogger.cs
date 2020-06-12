// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Azure.Cosmos;

namespace Microsoft.Health.CosmosDb.Features.Queries
{
    /// <summary>
    /// Logger used for logging <see cref="FhirDocumentQuery{T}"/>.
    /// </summary>
    public interface IDocumentQueryLogger
    {
        /// <summary>
        /// Logs the query execution.
        /// </summary>
        /// <param name="queryId">The query id for correlating query execution.</param>
        /// <param name="sqlQuerySpec">The SQL query.</param>
        /// <param name="continuationToken">The continuation token.</param>
        /// <param name="maxItemCount">The max item count.</param>
        void LogQueryExecution(Guid queryId, QueryDefinition sqlQuerySpec, string continuationToken, int? maxItemCount);

        /// <summary>
        /// Logs the query execution result.
        /// </summary>
        /// <param name="queryId">The query id for correlating query execution.</param>
        /// <param name="activityId">The activity id returned by the Cosmos DB.</param>
        /// <param name="requestCharge">The request charge for the execution.</param>
        /// <param name="continuationToken">The continuation token for paging.</param>
        /// <param name="eTag">The ETag for the result.</param>
        /// <param name="count">The number of documents returned.</param>
        /// <param name="exception">The exception if any.</param>
        void LogQueryExecutionResult(
            Guid queryId,
            string activityId,
            double requestCharge,
            string continuationToken,
            string eTag,
            int count,
            Exception exception = null);
    }
}
