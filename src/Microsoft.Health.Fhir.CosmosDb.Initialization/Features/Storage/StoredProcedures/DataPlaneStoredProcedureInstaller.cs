// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Azure.Cosmos;
using Microsoft.Health.Fhir.CosmosDb.Core.Features.Storage.StoredProcedures;

namespace Microsoft.Health.Fhir.CosmosDb.Initialization.Features.Storage.StoredProcedures
{
    public sealed class DataPlaneStoredProcedureInstaller : IStoredProcedureInstaller
    {
        private readonly IEnumerable<IStoredProcedureMetadata> _storeProceduresMetadata;

        public DataPlaneStoredProcedureInstaller(IEnumerable<IStoredProcedureMetadata> storedProcedures)
        {
            EnsureArg.IsNotNull(storedProcedures, nameof(storedProcedures));

            _storeProceduresMetadata = storedProcedures;
        }

        public async Task ExecuteAsync(Container container, CancellationToken cancellationToken)
        {
            foreach (IStoredProcedureMetadata storedProc in _storeProceduresMetadata)
            {
                try
                {
                    await container.Scripts.ReadStoredProcedureAsync(storedProc.FullName, cancellationToken: cancellationToken);
                }
                catch (CosmosException e) when (e.StatusCode == HttpStatusCode.NotFound)
                {
                    await container.Scripts.CreateStoredProcedureAsync(storedProc.ToStoredProcedureProperties(), cancellationToken: cancellationToken);
                }
            }
        }
    }
}
