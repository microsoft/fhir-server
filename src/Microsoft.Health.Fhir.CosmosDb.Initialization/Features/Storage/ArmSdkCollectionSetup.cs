using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.CosmosDB;
using EnsureThat;
using Microsoft.Health.Fhir.CosmosDb.Core.Configs;
using Microsoft.Health.Fhir.CosmosDb.Core.Features.Storage;
using Microsoft.Health.Fhir.CosmosDb.Core.Features.Storage.StoredProcedures;
using Polly;

namespace Microsoft.Health.Fhir.CosmosDb.Initialization.Features.Storage;

public class ArmSdkCollectionSetup : ICollectionSetup
{
    private readonly ArmClient _armClient;
    private readonly CosmosCollectionConfiguration _cosmosCollectionConfiguration;
    private readonly CosmosDataStoreConfiguration _cosmosDataStoreConfiguration;
    private readonly IEnumerable<IStoredProcedureMetadata> _storeProceduresMetadata;

    public ArmSdkCollectionSetup(
        CosmosCollectionConfiguration cosmosCollectionConfiguration,
        CosmosDataStoreConfiguration cosmosDataStoreConfiguration,
        IEnumerable<IStoredProcedureMetadata> storedProcedures)
    {
        EnsureArg.IsNotNull(storedProcedures, nameof(storedProcedures));

        _cosmosCollectionConfiguration = cosmosCollectionConfiguration;
        _cosmosDataStoreConfiguration = cosmosDataStoreConfiguration;
        _storeProceduresMetadata = storedProcedures;
        _armClient = new ArmClient(new DefaultAzureCredential());
    }

    public Task CreateDatabaseAsync(AsyncPolicy retryPolicy, CancellationToken cancellationToken)
    {
        throw new System.NotImplementedException();
    }

    public async Task CreateCollectionAsync(IEnumerable<ICollectionInitializer> collectionInitializers, AsyncPolicy retryPolicy, CancellationToken cancellationToken = default)
    {
        var cosmos = _armClient.GetCosmosDBSqlDatabaseResource(ResourceIdentifier.Parse("abc"));
        var c = await cosmos.GetCosmosDBSqlContainerAsync(_cosmosCollectionConfiguration.CollectionId, cancellationToken);

        var meta = _storeProceduresMetadata.ToList();

        var existing = c.Value.GetCosmosDBSqlStoredProcedures().Select(x => x.Data.Resource.StoredProcedureName).ToList();

        foreach (var storedProc in meta)
        {
            if (!existing.Contains(storedProc.FullName))
            {
                c.Value.UpdateAsync()
            }
        }
    }

    public Task UpdateFhirCollectionSettingsAsync(CancellationToken cancellationToken)
    {
        throw new System.NotImplementedException();
    }
}
