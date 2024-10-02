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
using Microsoft.Health.Fhir.CosmosDb.Features.Storage.Operations.Export;
using Microsoft.Health.Fhir.CosmosDb.Initialization.Features.Storage.StoredProcedures;
using Microsoft.Health.Fhir.CosmosDb.Initialization.Features.Storage.StoredProcedures.AcquireExportJobs;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage.StoredProcedures.AcquireExportJobs
{
    internal class AcquireExportJobs : StoredProcedureBase
    {
        public AcquireExportJobs()
            : base(new AcquireExportJobsMetadata())
        {
        }

        public async Task<StoredProcedureExecuteResponse<IReadOnlyCollection<CosmosExportJobRecordWrapper>>> ExecuteAsync(
            Scripts client,
            ushort numberOfJobsToAcquire,
            ushort jobHeartbeatTimeoutThresholdInSeconds,
            CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(client, nameof(client));

            return await ExecuteStoredProcAsync<IReadOnlyCollection<CosmosExportJobRecordWrapper>>(
                client,
                CosmosDbExportConstants.ExportJobPartitionKey,
                cancellationToken,
                numberOfJobsToAcquire,
                jobHeartbeatTimeoutThresholdInSeconds);
        }
    }
}
