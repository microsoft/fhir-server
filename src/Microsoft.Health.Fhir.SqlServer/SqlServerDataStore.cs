// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.SqlServer.Configs;
using Newtonsoft.Json;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.SqlServer
{
    public class SqlServerDataStore : IDataStore, IProvideCapability
    {
        private readonly SqlServerDataStoreConfiguration _configuration;
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager;
        private Dictionary<ResourceType, short> _resourceTypeToId;
        private Dictionary<string, short> _searchParamUrlToId;

        public SqlServerDataStore(SqlServerDataStoreConfiguration configuration, ISearchParameterDefinitionManager searchParameterDefinitionManager)
        {
            EnsureArg.IsNotNull(configuration, nameof(configuration));
            EnsureArg.IsNotNull(searchParameterDefinitionManager, nameof(searchParameterDefinitionManager));

            _configuration = configuration;
            _searchParameterDefinitionManager = searchParameterDefinitionManager;

            InitializeStore().GetAwaiter().GetResult();
        }

        private async Task InitializeStore()
        {
            using (var connection = new SqlConnection(_configuration.ConnectionString))
            {
                await connection.OpenAsync();
                SqlCommand sqlCommand = connection.CreateCommand();

                sqlCommand.CommandText =
                    @"INSERT INTO dbo.ResourceType (Name) 
                      SELECT value FROM string_split(@p1, ',')
                      EXCEPT SELECT Name from dbo.ResourceType; 
                      
                      SELECT ResourceTypePK, Name FROM dbo.ResourceType;

                      INSERT INTO dbo.SearchParam 
                          ([Name], [Uri])
                      SELECT * FROM  OPENJSON (@p2) 
                      WITH ([Name] varchar(200) '$[0]' , [Uri] varchar(200) '$[1]')
                      EXCEPT SELECT Name, Uri from dbo.SearchParam;

                      SELECT SearchParamPK, Uri FROM dbo.SearchParam;";

                sqlCommand.Parameters.AddWithValue("@p1", string.Join(",", ModelInfo.SupportedResources));
                sqlCommand.Parameters.AddWithValue("@p2", JsonConvert.SerializeObject(_searchParameterDefinitionManager.AllSearchParameters.Select(p => new[] { p.Name, p.Url }).ToArray()));

                using (SqlDataReader reader = await sqlCommand.ExecuteReaderAsync())
                {
                    _resourceTypeToId = new Dictionary<ResourceType, short>();
                    while (await reader.ReadAsync())
                    {
                        _resourceTypeToId.Add(
                            (ResourceType)Enum.Parse(typeof(ResourceType), reader.GetString(1)),
                            reader.GetInt16(0));
                    }

                    await reader.NextResultAsync();

                    _searchParamUrlToId = new Dictionary<string, short>(StringComparer.Ordinal);
                    while (await reader.ReadAsync())
                    {
                        _searchParamUrlToId.Add(
                            reader.GetString(1),
                            reader.GetInt16(0));
                    }
                }
            }
        }

        public Task<ResourceWrapper> GetAsync(ResourceKey key, CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new System.NotImplementedException();
        }

        public Task HardDeleteAsync(ResourceKey key, CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new System.NotImplementedException();
        }

        public async Task<UpsertOutcome> UpsertAsync(ResourceWrapper resource, WeakETag weakETag, bool isCreate, bool allowCreate, bool keepHistory, CancellationToken cancellationToken = default)
        {
            using (var connection = new SqlConnection(_configuration.ConnectionString))
            {
                await connection.OpenAsync(cancellationToken);
            }

            return new UpsertOutcome(resource, SaveOutcomeType.Created);
        }

        public void Build(ListedCapabilityStatement statement)
        {
            EnsureArg.IsNotNull(statement, nameof(statement));

            foreach (var resource in ModelInfo.SupportedResources)
            {
                var resourceType = (ResourceType)Enum.Parse(typeof(ResourceType), resource);
                statement.BuildRestResourceComponent(resourceType, builder =>
                {
                    builder.Versioning.Add(CapabilityStatement.ResourceVersionPolicy.NoVersion);
                    builder.Versioning.Add(CapabilityStatement.ResourceVersionPolicy.Versioned);
                    builder.Versioning.Add(CapabilityStatement.ResourceVersionPolicy.VersionedUpdate);
                    builder.ReadHistory = true;
                    builder.UpdateCreate = true;
                });
            }
        }
    }
}
