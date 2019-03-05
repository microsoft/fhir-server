// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using Microsoft.Health.ControlPlane.CosmosDb.Features.Storage.Rbac;
using Microsoft.Health.CosmosDb.Features.Storage.StoredProcedures;

namespace Microsoft.Health.ControlPlane.CosmosDb.Features.Storage.StoredProcedures.HardDelete
{
    internal class HardDeleteIdentityProvider : StoredProcedureBase, IControlPlaneStoredProcedure
    {
        public async Task<StoredProcedureResponse<IList<string>>> Execute(IDocumentClient client, Uri collection, string id, string eTag, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(client, nameof(client));
            EnsureArg.IsNotNull(collection, nameof(collection));
            EnsureArg.IsNotNullOrWhiteSpace(id, nameof(id));

            return await ExecuteStoredProc<IList<string>>(client, collection, CosmosIdentityProvider.IdentityProviderPartition, cancellationToken, id, eTag);
        }
    }
}
