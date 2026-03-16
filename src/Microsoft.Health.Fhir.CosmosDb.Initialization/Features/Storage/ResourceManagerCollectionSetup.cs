// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.CosmosDb.Core.Configs;
using Microsoft.Health.Fhir.CosmosDb.Core.Features.Storage;
using Microsoft.Health.Fhir.CosmosDb.Core.Features.Storage.StoredProcedures;
using Microsoft.Health.Fhir.CosmosDb.Core.Features.Storage.Versioning;
using Polly;

namespace Microsoft.Health.Fhir.CosmosDb.Initialization.Features.Storage;

/// <summary>
/// Sets up the Cosmos FHIR Database using the ResourceManager SDK and ManagedIdentity.
/// Requires the MI connection to have 'Cosmos DB Operator' permissions.
/// </summary>
public class ResourceManagerCollectionSetup : ICollectionSetup
{
    private readonly ILogger<ResourceManagerCollectionSetup> _logger;
    private readonly ArmClient _armClient;
    private readonly IOptionsMonitor<CosmosCollectionConfiguration> _cosmosCollectionConfiguration;
    private readonly CosmosDataStoreConfiguration _cosmosDataStoreConfiguration;
    private readonly IEnumerable<IStoredProcedureMetadata> _storeProceduresMetadata;
    private readonly ResourceIdentifier _resourceIdentifier;
    private CosmosDBSqlDatabaseResource _database;
    private AzureLocation? _location;
    private readonly CosmosDBAccountResource _account;
    private const string FhirServerResourceManagerDataStoreResourceId = "FhirServer:ResourceManager:DataStoreResourceId";

    public ResourceManagerCollectionSetup(
        IOptionsMonitor<CosmosCollectionConfiguration> cosmosCollectionConfiguration,
        CosmosDataStoreConfiguration cosmosDataStoreConfiguration,
        IConfiguration genericConfiguration,
        IEnumerable<IStoredProcedureMetadata> storedProcedures,
        TokenCredential tokenCredential,
        Func<TokenCredential, ArmClient> armClientFactory,
        ILogger<ResourceManagerCollectionSetup> logger)
    {
        EnsureArg.IsNotNull(storedProcedures, nameof(storedProcedures));

        _cosmosCollectionConfiguration = EnsureArg.IsNotNull(cosmosCollectionConfiguration, nameof(cosmosCollectionConfiguration));
        _cosmosDataStoreConfiguration = EnsureArg.IsNotNull(cosmosDataStoreConfiguration, nameof(cosmosDataStoreConfiguration));
        _storeProceduresMetadata = EnsureArg.IsNotNull(storedProcedures, nameof(storedProcedures));
        _logger = EnsureArg.IsNotNull(logger, nameof(logger));
        EnsureArg.IsNotNull(tokenCredential, nameof(tokenCredential));
        EnsureArg.IsNotNull(armClientFactory, nameof(armClientFactory));

        var dataStoreResourceId = EnsureArg.IsNotNullOrWhiteSpace(
                genericConfiguration.GetValue(FhirServerResourceManagerDataStoreResourceId, string.Empty),
                nameof(genericConfiguration),
                fn => fn.WithMessage($"{FhirServerResourceManagerDataStoreResourceId} must be set."));

        _armClient = armClientFactory(tokenCredential);
        _resourceIdentifier = ResourceIdentifier.Parse(dataStoreResourceId);
        _account = _armClient.GetCosmosDBAccountResource(_resourceIdentifier);
    }

    private CosmosDBSqlDatabaseResource Database
    {
        get
        {
            if (_database != null)
            {
                return _database;
            }

            _database = _account.GetCosmosDBSqlDatabase(_cosmosDataStoreConfiguration.DatabaseId);
            return _database;
        }
    }

    /// <summary>
    /// Reads the location from an existing CosmosDB account.
    /// </summary>
    private AzureLocation Location
    {
        get
        {
            if (_location.HasValue)
            {
                return _location.Value;
            }

            _location = _account.Get().Value.Data.Location;
            return _location.Value;
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

        _logger.LogInformation("Checking if '{DatabaseId}' exists.", _cosmosDataStoreConfiguration.DatabaseId);

        if (!(await databaseCollection.ExistsAsync(_cosmosDataStoreConfiguration.DatabaseId, cancellationToken)).Value)
        {
            _logger.LogInformation("Database '{DatabaseId}' was not found, creating.", _cosmosDataStoreConfiguration.DatabaseId);

            await databaseCollection.CreateOrUpdateAsync(
                WaitUntil.Completed,
                _cosmosDataStoreConfiguration.DatabaseId,
                new CosmosDBSqlDatabaseCreateOrUpdateContent(
                    Location,
                    new CosmosDBSqlDatabaseResourceInfo(_cosmosDataStoreConfiguration.DatabaseId)),
                cancellationToken);
        }
        else
        {
            _logger.LogInformation("Database '{DatabaseId}' found.", _cosmosDataStoreConfiguration.DatabaseId);
        }
    }

    public async Task CreateCollectionAsync(IEnumerable<ICollectionInitializer> collectionInitializers, AsyncPolicy retryPolicy, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Checking if '{CollectionId}' exists.", CollectionId);

        NullableResponse<CosmosDBSqlContainerResource> containerResponse = await Database.GetCosmosDBSqlContainers().GetIfExistsAsync(CollectionId, cancellationToken);
        CosmosDBSqlContainerResource container = containerResponse.HasValue ? containerResponse.Value : null;

        if (container == null)
        {
            _logger.LogInformation("Collection '{CollectionId}' was not found, creating.", CollectionId);
            CosmosDBSqlContainerResourceInfo containerResourceInfo = GetContainerResourceInfo();

            var content = new CosmosDBSqlContainerCreateOrUpdateContent(
                Location,
                containerResourceInfo);

            CosmosDBSqlContainerCollection containerCollection = Database.GetCosmosDBSqlContainers();
            ArmOperation<CosmosDBSqlContainerResource> newContainerResponse = await containerCollection.CreateOrUpdateAsync(WaitUntil.Completed, CollectionId, content, cancellationToken);
            container = newContainerResponse.Value;

            var throughput =
                _cosmosCollectionConfiguration.Get(Core.Constants.CollectionConfigurationName)
                    .InitialCollectionThroughput;

            if (throughput.HasValue)
            {
                _logger.LogInformation("Updating container throughput to '{Throughput}' RUs.", throughput);
                await container
                    .GetCosmosDBSqlContainerThroughputSetting()
                    .CreateOrUpdateAsync(
                        WaitUntil.Started, // Throughput provisioning can be async
                        new ThroughputSettingsUpdateData(
                            Location,
                            new ThroughputSettingsResourceInfo
                            {
                                Throughput = throughput,
                            }),
                        cancellationToken);
            }
        }
        else
        {
            _logger.LogInformation("Collection '{CollectionId}' found.", CollectionId);
        }
    }

    public async Task InstallStoredProcs(CancellationToken cancellationToken)
    {
        CosmosDBSqlContainerResource containerResponse = await Database.GetCosmosDBSqlContainers().GetAsync(CollectionId, cancellationToken);
        CosmosDBSqlStoredProcedureCollection cosmosDbSqlStoredProcedures = containerResponse.GetCosmosDBSqlStoredProcedures();

        var existing = cosmosDbSqlStoredProcedures
            .Select(x => x.Data.Resource.StoredProcedureName)
            .ToList();

        var storedProcsNeedingInstall = _storeProceduresMetadata
            .Where(storedProc => !existing.Contains(storedProc.FullName))
            .ToList();

        foreach (IStoredProcedureMetadata storedProc in storedProcsNeedingInstall)
        {
            _logger.LogInformation("Installing StoredProc '{StoredProcFullName}'.", storedProc.FullName);

            var cosmosDbSqlStoredProcedureResource = new CosmosDBSqlStoredProcedureResourceInfo(storedProc.FullName)
            {
                Body = storedProc.ToStoredProcedureProperties().Body,
            };

            var storedProcedureCreateOrUpdateContent = new CosmosDBSqlStoredProcedureCreateOrUpdateContent(Location, cosmosDbSqlStoredProcedureResource);
            await cosmosDbSqlStoredProcedures.CreateOrUpdateAsync(WaitUntil.Completed, storedProc.FullName, storedProcedureCreateOrUpdateContent, cancellationToken);
        }
    }

    public async Task UpdateFhirCollectionSettingsAsync(CollectionVersion version, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Updating collection settings.");

        CosmosDBSqlContainerResourceInfo containerResourceInfo = GetContainerResourceInfo();

        var content = new CosmosDBSqlContainerCreateOrUpdateContent(
            Location,
            containerResourceInfo);

        CosmosDBSqlContainerCollection containers = Database.GetCosmosDBSqlContainers();
        await containers.CreateOrUpdateAsync(WaitUntil.Completed, CollectionId, content, cancellationToken);
    }

    private CosmosDBSqlContainerResourceInfo GetContainerResourceInfo()
    {
        return new CosmosDBSqlContainerResourceInfo(CollectionId)
        {
            PartitionKey = new CosmosDBContainerPartitionKey
            {
                Paths = { $"/{KnownDocumentProperties.PartitionKey}" },
                Kind = CosmosDBPartitionKind.Hash,
            },
            IndexingPolicy = CreateCosmosDbIndexingPolicy(),
            DefaultTtl = -1,
        };
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
