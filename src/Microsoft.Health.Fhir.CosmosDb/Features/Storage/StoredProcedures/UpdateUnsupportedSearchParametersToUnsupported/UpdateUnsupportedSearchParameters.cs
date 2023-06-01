// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Azure.Cosmos.Scripts;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage.Operations.Reindex;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage.StoredProcedures.UpdateUnsupportedSearchParametersToUnsupported
{
    internal class UpdateUnsupportedSearchParameters : StoredProcedureBase
    {
        public async Task<StoredProcedureExecuteResponse<string>> Execute(Scripts client, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(client, nameof(client));

            return await ExecuteStoredProc<string>(client, CosmosDbReindexConstants.SearchParameterStatusPartitionKey, cancellationToken);
        }
    }
}
