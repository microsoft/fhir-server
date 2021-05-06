// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Azure.Cosmos;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage.Versioning;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage.StoredProcedures
{
    public class StoredProcedureInstaller : ICollectionUpdater
    {
        private readonly IEnumerable<IStoredProcedure> _storedProcedures;

        public StoredProcedureInstaller(IEnumerable<IStoredProcedure> storedProcedures)
        {
            EnsureArg.IsNotNull(storedProcedures, nameof(storedProcedures));

            _storedProcedures = storedProcedures;
        }

        public async Task ExecuteAsync(Container container)
        {
            foreach (IStoredProcedure storedProc in _storedProcedures)
            {
                try
                {
                    await container.Scripts.ReadStoredProcedureAsync(storedProc.FullName);
                }
                catch (CosmosException e) when (e.StatusCode == HttpStatusCode.NotFound)
                {
                    await container.Scripts.CreateStoredProcedureAsync(storedProc.ToStoredProcedureProperties());
                }
            }
        }
    }
}
