// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos.Scripts;

namespace Microsoft.Health.Fhir.CosmosDb.Core.Features.Storage.StoredProcedures
{
    public interface IStoredProcedure
    {
         Task<StoredProcedureExecuteResponse<T>> ExecuteStoredProcAsync<T>(Scripts client, string partitionId, CancellationToken cancellationToken, params object[] parameters);
    }
}
