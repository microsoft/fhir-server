// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Azure.Cosmos.Scripts;
using Microsoft.Health.Fhir.CosmosDb.Core.Features.Storage.StoredProcedures;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage.Operations.Reindex;
using Microsoft.Health.Fhir.CosmosDb.Initialization.Features.Storage.StoredProcedures.AcquireReindexJobs;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage.StoredProcedures.AcquireReindexJobs
{
    internal class AcquireReindexJobs : StoredProcedureBase
    {
        public AcquireReindexJobs()
            : base(new AcquireReindexJobsMetadata())
        {
        }

        public async Task<StoredProcedureExecuteResponse<IReadOnlyCollection<CosmosReindexJobRecordWrapper>>> ExecuteAsync(
            Scripts client,
            ushort maximumNumberOfConcurrentJobsAllowed,
            ushort jobHeartbeatTimeoutThresholdInSeconds,
            CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(client, nameof(client));

            return await ExecuteStoredProcAsync<IReadOnlyCollection<CosmosReindexJobRecordWrapper>>(
                client,
                CosmosDbReindexConstants.ReindexJobPartitionKey,
                cancellationToken,
                maximumNumberOfConcurrentJobsAllowed,
                jobHeartbeatTimeoutThresholdInSeconds);
        }
    }
}
