// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Scripts;
using Microsoft.Health.Core.Extensions;

namespace Microsoft.Health.Fhir.CosmosDb.Core.Features.Storage.StoredProcedures
{
    public abstract class StoredProcedureBase : IStoredProcedure
    {
        private readonly IStoredProcedureMetadata _storedProcedureMetadata;

        protected StoredProcedureBase(IStoredProcedureMetadata storedProcedureMetadata)
        {
            EnsureArg.IsNotNull(storedProcedureMetadata, nameof(storedProcedureMetadata));

            _storedProcedureMetadata = storedProcedureMetadata;
        }

        public string FullName => _storedProcedureMetadata.FullName;

        public async Task<StoredProcedureExecuteResponse<T>> ExecuteStoredProcAsync<T>(Scripts client, string partitionId, CancellationToken cancellationToken, params object[] parameters)
        {
            EnsureArg.IsNotNull(client, nameof(client));
            EnsureArg.IsNotNull(partitionId, nameof(partitionId));

            StoredProcedureExecuteResponse<T> results = await client.ExecuteStoredProcedureAsync<T>(
                    FullName,
                    new PartitionKey(partitionId),
                    parameters,
                    cancellationToken: cancellationToken);

            return results;
        }
    }
}
