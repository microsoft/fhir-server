// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Azure.Cosmos.Scripts;
using Microsoft.Health.CosmosDb.Features.Storage.StoredProcedures;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage.StoredProcedures.Upsert
{
    internal class UpsertWithHistory : StoredProcedureBase, IFhirStoredProcedure
    {
        public async Task<UpsertWithHistoryModel> Execute(Scripts client, FhirCosmosResourceWrapper resource, string matchVersionId, bool allowCreate, bool keepHistory, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(client, nameof(client));
            EnsureArg.IsNotNull(resource, nameof(resource));

            StoredProcedureExecuteResponse<UpsertWithHistoryModel> results =
                await ExecuteStoredProc<UpsertWithHistoryModel>(client, resource.PartitionKey, cancellationToken, resource, matchVersionId, allowCreate, keepHistory);

            return results.Resource;
        }
    }
}
