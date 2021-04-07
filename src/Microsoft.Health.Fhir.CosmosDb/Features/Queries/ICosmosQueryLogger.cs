// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Azure.Cosmos;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Queries
{
    /// <summary>
    /// Logger used for logging <see cref="FhirDocumentQuery{T}"/>.
    /// </summary>
    public interface ICosmosQueryLogger
    {
        /// <summary>
        /// Logs the query execution.
        /// </summary>
        /// <param name="sqlQuerySpec">The SQL query.</param>
        /// <param name="partitionKey">The partitionKey of the query</param>
        /// <param name="continuationToken">The continuation token.</param>
        /// <param name="maxItemCount">The max item count.</param>
        /// <param name="maxConcurrency">Corresponds to <see cref="QueryRequestOptions.MaxConcurrency"/></param>
        void LogQueryExecution(QueryDefinition sqlQuerySpec, string partitionKey, string continuationToken, int? maxItemCount, int? maxConcurrency);

        /// <summary>
        /// Logs the query execution result.
        /// </summary>
        /// <param name="activityId">The activity id returned by the Cosmos DB.</param>
        /// <param name="requestCharge">The request charge for the execution.</param>
        /// <param name="continuationToken">The continuation token for paging.</param>
        /// <param name="count">The number of documents returned.</param>
        /// <param name="durationMs">The database-reported duration of the query in milliseconds</param>
        /// <param name="partitionKeyRangeId">The ID of the physical partition</param>
        /// <param name="exception">The exception if any.</param>
        void LogQueryExecutionResult(
            string activityId,
            double requestCharge,
            string continuationToken,
            int count,
            double durationMs,
            string partitionKeyRangeId,
            Exception exception = null);
    }
}
