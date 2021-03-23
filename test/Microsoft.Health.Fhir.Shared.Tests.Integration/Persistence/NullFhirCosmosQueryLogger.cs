// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Azure.Cosmos;
using Microsoft.Health.Fhir.CosmosDb.Features.Queries;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    public class NullFhirCosmosQueryLogger : ICosmosQueryLogger
    {
        public static ICosmosQueryLogger Instance { get; } = new NullFhirCosmosQueryLogger();

        public void LogQueryExecution(QueryDefinition sqlQuerySpec, string partitionKey, string continuationToken, int? maxItemCount, int? maxConcurrency)
        {
        }

        public void LogQueryExecutionResult(string activityId, double requestCharge, string continuationToken, int count, double durationMs, string partitionKeyRangeId, Exception exception = null)
        {
        }
    }
}
