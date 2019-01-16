// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using Microsoft.Health.ControlPlane.CosmosDb.Features.Storage.Rbac;
using Microsoft.Health.CosmosDb.Features.Storage.StoredProcedures;
using Microsoft.Health.Extensions.DependencyInjection;

namespace Microsoft.Health.ControlPlane.CosmosDb.Features.Storage.StoredProcedures.HardDelete
{
    internal class HardDeleteRole : StoredProcedureBase, IControlPlaneStoredProcedure
    {
        public async Task<StoredProcedureResponse<IList<string>>> Execute(IScoped<IDocumentClient> client, Uri collection,  string id)
        {
            EnsureArg.IsNotNull(client, nameof(client));
            EnsureArg.IsNotNull(collection, nameof(collection));
            EnsureArg.IsNotNull(id, nameof(id));

            return await ExecuteStoredProc<IList<string>>(client.Value, collection, CosmosRole.RolePartition, id);
        }
    }
}
