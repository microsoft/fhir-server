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

namespace Microsoft.Health.Fhir.CosmosDb.Initialization.Features.Storage.StoredProcedures
{
    public class DataPlaneStoredProcedureInstaller : IStoredProcedureInstaller
    {
        private readonly IEnumerable<IStoredProcedureMetadata> _storedProcedures;

        // TODO: refactor constructor to have dependency on container
        public DataPlaneStoredProcedureInstaller(IEnumerable<IStoredProcedureMetadata> storedProcedures)
        {
            EnsureArg.IsNotNull(storedProcedures, nameof(storedProcedures));

            _storedProcedures = storedProcedures;
        }

        // TODO: refactor method to have dependency on IReadOnlyList<Istoredproceduremetada>
        public async Task ExecuteAsync(Container container, CancellationToken cancellationToken)
        {
            foreach (IStoredProcedureMetadata storedProc in _storedProcedures)
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
