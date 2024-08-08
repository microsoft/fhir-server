// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.CosmosDB;
using EnsureThat;
using Microsoft.Azure.Cosmos;
using Microsoft.Health.Fhir.CosmosDb.Core.Features.Storage.StoredProcedures;

namespace Microsoft.Health.Fhir.CosmosDb.Initialization.Features.Storage.StoredProcedures;

public class ResourceManagerStoredProcedureInstaller : IStoredProcedureInstaller
{
    private readonly IEnumerable<IStoredProcedureMetadata> _storeProceduresMetadata;
    private readonly ArmClient armClient;

    public ResourceManagerStoredProcedureInstaller(IEnumerable<IStoredProcedureMetadata> storedProcedures)
    {
        EnsureArg.IsNotNull(storedProcedures, nameof(storedProcedures));

        _storeProceduresMetadata = storedProcedures;

        armClient = new ArmClient(new DefaultAzureCredential());
    }

    public async Task ExecuteAsync(Container container, CancellationToken cancellationToken)
    {
        var cosmos = armClient.GetCosmosDBSqlDatabaseResource(ResourceIdentifier.Parse("abc"));
        var c = await cosmos.GetCosmosDBSqlContainerAsync(container., cancellationToken);
        c.Value.GetCosmosDBSqlStoredProcedures()
    }
}
