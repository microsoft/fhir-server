// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Microsoft.Health.CosmosDb.Features.Storage.Versioning;

namespace Microsoft.Health.CosmosDb.Features.Storage.StoredProcedures
{
    public class StoredProcedureInstaller : ICollectionUpdater
    {
        public async Task ExecuteAsync(Container client)
        {
            foreach (IStoredProcedure storedProc in GetStoredProcedures())
            {
                try
                {
                    await client.Scripts.ReadStoredProcedureAsync(storedProc.FullName);
                }
                catch (CosmosException e) when (e.StatusCode == HttpStatusCode.NotFound)
                {
                    await client.Scripts.CreateStoredProcedureAsync(storedProc.AsStoredProcedure());
                }
            }
        }

        public virtual IEnumerable<IStoredProcedure> GetStoredProcedures()
        {
            var buildInProcs = typeof(IStoredProcedure).Assembly
                .GetTypes()
                .Where(x => !x.IsAbstract && typeof(IStoredProcedure).IsAssignableFrom(x))
                .ToArray();

            return buildInProcs.Select(type => (IStoredProcedure)Activator.CreateInstance(type));
        }
    }
}
