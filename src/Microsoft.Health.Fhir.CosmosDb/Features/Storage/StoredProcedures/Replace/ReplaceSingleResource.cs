// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Azure.Cosmos.Scripts;
using Microsoft.Health.Fhir.CosmosDb.Core.Features.Storage.StoredProcedures;
using Microsoft.Health.Fhir.CosmosDb.Initialization.Features.Storage.StoredProcedures.Replace;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage.StoredProcedures.Replace
{
    internal class ReplaceSingleResource : StoredProcedureBase
    {
        public ReplaceSingleResource()
            : base(new ReplaceSingleResourceMetadata())
        {
        }

        public async Task<FhirCosmosResourceWrapper> Execute(Scripts client, FhirCosmosResourceWrapper resource, string matchVersionId, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(client, nameof(client));
            EnsureArg.IsNotNull(resource, nameof(resource));

            StoredProcedureExecuteResponse<FhirCosmosResourceWrapper> result =
                await ExecuteStoredProcAsync<FhirCosmosResourceWrapper>(
                    client,
                    resource.PartitionKey,
                    cancellationToken,
                    resource,
                    matchVersionId);

            return result.Resource;
        }
    }
}
