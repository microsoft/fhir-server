// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Azure.Cosmos.Scripts;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage.Operations.Task;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage.StoredProcedures.GetTask
{
    internal class GetTask : StoredProcedureBase
    {
        public async Task<StoredProcedureExecuteResponse<CosmosTaskInfoWrapper>> ExecuteAsync(
            Scripts client,
            string taskId,
            CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(client, nameof(client));

            return await ExecuteStoredProc<CosmosTaskInfoWrapper>(
                client,
                CosmosDbTaskConstants.TaskPartitionKey,
                cancellationToken,
                taskId);
        }
    }
}
