// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.CosmosDB;
using Azure.ResourceManager.CosmosDB.Models;
using EnsureThat;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.CosmosDb.Core.Configs;
using Microsoft.Health.Fhir.CosmosDb.Core.Features.Storage;
using Microsoft.Health.Fhir.CosmosDb.Core.Features.Storage.StoredProcedures;
using Polly;

namespace Microsoft.Health.Fhir.CosmosDb.Initialization.Features.Storage;

public class ArmSdkCollectionSetup : ICollectionSetup
{
    private readonly ArmClient _armClient;
    private readonly IOptionsMonitor<CosmosCollectionConfiguration> _cosmosCollectionConfiguration;
    private readonly CosmosDataStoreConfiguration _cosmosDataStoreConfiguration;
    private readonly IEnumerable<IStoredProcedureMetadata> _storeProceduresMetadata;
    private readonly ResourceIdentifier _resourceIdentifier;
    private CosmosDBSqlDatabaseResource _database;

    public ArmSdkCollectionSetup(
        IOptionsMonitor<CosmosCollectionConfiguration> cosmosCollectionConfiguration,
        CosmosDataStoreConfiguration cosmosDataStoreConfiguration,
        IConfiguration genericConfiguration,
        IEnumerable<IStoredProcedureMetadata> storedProcedures)
    {
        EnsureArg.IsNotNull(storedProcedures, nameof(storedProcedures));

        _cosmosCollectionConfiguration = EnsureArg.IsNotNull(cosmosCollectionConfiguration, nameof(cosmosCollectionConfiguration));
        _cosmosDataStoreConfiguration = EnsureArg.IsNotNull(cosmosDataStoreConfiguration, nameof(cosmosDataStoreConfiguration));
        _storeProceduresMetadata = EnsureArg.IsNotNull(storedProcedures, nameof(storedProcedures));
        _armClient = new ArmClient(new DefaultAzureCredential());
        var dataStoreResourceId = genericConfiguration.GetValue("FhirServer:ResourceManager:DataStoreResourceId", string.Empty);
        _resourceIdentifier = ResourceIdentifier.Parse(dataStoreResourceId);
    }

    private CosmosDBSqlDatabaseResource Database
    {
        get
        {
            if (_database != null)
            {
                return _database;
            }

            CosmosDBAccountResource account = _armClient.GetCosmosDBAccountResource(_resourceIdentifier);
            _database = account.GetCosmosDBSqlDatabase(_cosmosDataStoreConfiguration.DatabaseId);
            return _database;
        }
    }

    private string CollectionId
    {
        get
        {
            return _cosmosCollectionConfiguration.Get(Core.Constants.CollectionConfigurationName).CollectionId;
        }
    }

    public async Task CreateDatabaseAsync(AsyncPolicy retryPolicy, CancellationToken cancellationToken)
    {
        CosmosDBAccountResource account = _armClient.GetCosmosDBAccountResource(_resourceIdentifier);
        CosmosDBSqlDatabaseCollection databaseCollection = account.GetCosmosDBSqlDatabases();

        if (!(await databaseCollection.ExistsAsync(_cosmosDataStoreConfiguration.DatabaseId, cancellationToken)).Value)
        {
            await databaseCollection.CreateOrUpdateAsync(
                WaitUntil.Completed,
                _cosmosDataStoreConfiguration.DatabaseId,
                new CosmosDBSqlDatabaseCreateOrUpdateContent(
                    _resourceIdentifier.Location.GetValueOrDefault(),
                    new CosmosDBSqlDatabaseResourceInfo(_cosmosDataStoreConfiguration.DatabaseId)),
                cancellationToken);
        }
    }

    public async Task CreateCollectionAsync(IEnumerable<ICollectionInitializer> collectionInitializers, AsyncPolicy retryPolicy, CancellationToken cancellationToken = default)
    {
        Response<CosmosDBSqlContainerResource> containers = await Database.GetCosmosDBSqlContainerAsync(CollectionId, cancellationToken);

        if (containers.Value == null)
        {
            var containerResourceInfo = new CosmosDBSqlContainerResourceInfo(CollectionId)
            {
                PartitionKey = new CosmosDBContainerPartitionKey
                {
                    Paths = { $"/{KnownDocumentProperties.PartitionKey}" },
                    Kind = CosmosDBPartitionKind.Hash,
                },
                IndexingPolicy = CreateCosmosDbIndexingPolicy(),
            };

            var content = new CosmosDBSqlContainerCreateOrUpdateContent(
                new AzureLocation(_resourceIdentifier.Location.GetValueOrDefault()),
                containerResourceInfo);

            CosmosDBSqlContainerCollection containerCollection = Database.GetCosmosDBSqlContainers();
            await containerCollection.CreateOrUpdateAsync(WaitUntil.Completed, CollectionId, content, cancellationToken);

            containers = await Database.GetCosmosDBSqlContainerAsync(CollectionId, cancellationToken);

            var throughput =
                _cosmosCollectionConfiguration.Get(Core.Constants.CollectionConfigurationName)
                    .InitialCollectionThroughput;

            if (throughput.HasValue)
            {
                await containers.Value.GetCosmosDBSqlContainerThroughputSetting()
                    .CreateOrUpdateAsync(
                        WaitUntil.Completed,
                        new ThroughputSettingsUpdateData(
                            _resourceIdentifier.Location.GetValueOrDefault(),
                            new ThroughputSettingsResourceInfo
                            {
                                Throughput = throughput,
                            }),
                        cancellationToken);
            }
        }

        var meta = _storeProceduresMetadata.ToList();

        CosmosDBSqlStoredProcedureCollection cosmosDbSqlStoredProcedures = containers.Value.GetCosmosDBSqlStoredProcedures();
        var existing = cosmosDbSqlStoredProcedures.Select(x => x.Data.Resource.StoredProcedureName).ToList();

        foreach (IStoredProcedureMetadata storedProc in meta)
        {
            if (!existing.Contains(storedProc.FullName))
            {
                var cosmosDbSqlStoredProcedureResource = new CosmosDBSqlStoredProcedureResourceInfo(storedProc.FullName)
                {
                    Body = storedProc.ToStoredProcedureProperties().Body,
                };

                var storedProcedureCreateOrUpdateContent = new CosmosDBSqlStoredProcedureCreateOrUpdateContent(_resourceIdentifier.Location.GetValueOrDefault(), cosmosDbSqlStoredProcedureResource);
                await cosmosDbSqlStoredProcedures.CreateOrUpdateAsync(WaitUntil.Completed, storedProc.FullName, storedProcedureCreateOrUpdateContent, cancellationToken);
            }
        }
    }

    public async Task UpdateFhirCollectionSettingsAsync(CancellationToken cancellationToken)
    {
        var containerResourceInfo = new CosmosDBSqlContainerResourceInfo(CollectionId);

        containerResourceInfo.IndexingPolicy = CreateCosmosDbIndexingPolicy();

        var content = new CosmosDBSqlContainerCreateOrUpdateContent(
            new AzureLocation(_resourceIdentifier.Location.GetValueOrDefault()),
            containerResourceInfo);

        CosmosDBSqlContainerCollection containers = Database.GetCosmosDBSqlContainers();
        await containers.CreateOrUpdateAsync(WaitUntil.Completed, CollectionId, content, cancellationToken);
    }

    private static CosmosDBIndexingPolicy CreateCosmosDbIndexingPolicy()
    {
        var indexingPolicy = new CosmosDBIndexingPolicy
        {
            IsAutomatic = true, // Enable automatic indexing
            IndexingMode = CosmosDBIndexingMode.Consistent, // Choose indexing mode (Consistent or Lazy)
            IncludedPaths =
            {
                new CosmosDBIncludedPath
                {
                    Path = "/*", // Include all properties
                },
            },
            ExcludedPaths =
            {
                new CosmosDBExcludedPath
                {
                    Path = $"/{"rawResource"}/*", // Exclude properties under /excludedPath
                },
                new CosmosDBExcludedPath
                {
                    Path = "/\"_etag\"/?",
                },
            },
        };
        return indexingPolicy;
    }
}
