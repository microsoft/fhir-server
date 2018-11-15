// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Azure.Documents;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    public class NullFhirDocumentQueryLogger : IFhirDocumentQueryLogger
    {
        public static IFhirDocumentQueryLogger Instance { get; } = new NullFhirDocumentQueryLogger();

        public void LogQueryExecution(Guid queryId, SqlQuerySpec sqlQuerySpec, string continuationToken, int? maxItemCount)
        {
        }

        public void LogQueryExecutionResult(Guid queryId, string activityId, double requestCharge, string continuationToken, string eTag, int count, Exception exception = null)
        {
        }
    }
}
