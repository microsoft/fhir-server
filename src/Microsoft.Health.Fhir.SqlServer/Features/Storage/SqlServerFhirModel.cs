// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Configs;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Newtonsoft.Json;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    /// <summary>
    /// Maintains IDs for resource types, search parameters, systems, and codes for quantity search parameters.
    /// There are typically on the order of tens or hundreds of distinct values for each of these, but are reused
    /// many many times in the database. For more compact storage, we use IDs instead of the strings when referencing these.
    /// Also, because the number of distinct values is small, we can maintain all values in memory and avoid joins when querying.
    /// </summary>
    public sealed class SqlServerFhirModel : IDisposable
    {
        private readonly SqlServerDataStoreConfiguration _configuration;
        private readonly SchemaInformation _schemaInformation;
        private readonly ILogger<SqlServerFhirModel> _logger;
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager;
        private readonly SecurityConfiguration _securityConfiguration;
        private readonly RetryableInitializationOperation _initializationOperation;
        private Dictionary<string, short> _resourceTypeToId;
        private Dictionary<short, string> _resourceTypeIdToTypeName;
        private Dictionary<Uri, short> _searchParamUriToId;
        private ConcurrentDictionary<string, int> _systemToId;
        private ConcurrentDictionary<string, int> _quantityCodeToId;
        private Dictionary<string, byte> _claimNameToId;
        private Dictionary<string, byte> _compartmentTypeToId;

        public SqlServerFhirModel(
            SqlServerDataStoreConfiguration configuration,
            SchemaInformation schemaInformation,
            ISearchParameterDefinitionManager searchParameterDefinitionManager,
            IOptions<SecurityConfiguration> securityConfiguration,
            ILogger<SqlServerFhirModel> logger)
        {
            EnsureArg.IsNotNull(configuration, nameof(configuration));
            EnsureArg.IsNotNull(schemaInformation, nameof(schemaInformation));
            EnsureArg.IsNotNull(searchParameterDefinitionManager, nameof(searchParameterDefinitionManager));
            EnsureArg.IsNotNull(securityConfiguration?.Value, nameof(securityConfiguration));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _configuration = configuration;
            _schemaInformation = schemaInformation;
            _logger = logger;
            _searchParameterDefinitionManager = searchParameterDefinitionManager;
            _securityConfiguration = securityConfiguration.Value;

            _initializationOperation = new RetryableInitializationOperation(Initialize);
            if (schemaInformation.Current != null)
            {
                // kick off initialization so that it can be ready for requests. Errors will be observed by requests when they call the method.
                EnsureInitialized();
            }
        }

        public short GetResourceTypeId(string resourceTypeName)
        {
            ThrowIfNotInitialized();
            return _resourceTypeToId[resourceTypeName];
        }

        public bool TryGetResourceTypeId(string resourceTypeName, out short id)
        {
            ThrowIfNotInitialized();
            return _resourceTypeToId.TryGetValue(resourceTypeName, out id);
        }

        public string GetResourceTypeName(short resourceTypeId)
        {
            ThrowIfNotInitialized();
            return _resourceTypeIdToTypeName[resourceTypeId];
        }

        public byte GetClaimTypeId(string claimTypeName)
        {
            ThrowIfNotInitialized();
            return _claimNameToId[claimTypeName];
        }

        public short GetSearchParamId(Uri searchParamUri)
        {
            ThrowIfNotInitialized();
            return _searchParamUriToId[searchParamUri];
        }

        public byte GetCompartmentTypeId(string compartmentType)
        {
            ThrowIfNotInitialized();
            return _compartmentTypeToId[compartmentType];
        }

        public bool TryGetSystemId(string system, out int systemId)
        {
            ThrowIfNotInitialized();
            return _systemToId.TryGetValue(system, out systemId);
        }

        public int GetSystemId(string system)
        {
            ThrowIfNotInitialized();

            V1.SystemTable systemTable = V1.System;
            return GetStringId(_systemToId, system, systemTable, systemTable.SystemId, systemTable.Value);
        }

        public int GetQuantityCodeId(string code)
        {
            ThrowIfNotInitialized();

            V1.QuantityCodeTable quantityCodeTable = V1.QuantityCode;
            return GetStringId(_quantityCodeToId, code, quantityCodeTable, quantityCodeTable.QuantityCodeId, quantityCodeTable.Value);
        }

        public bool TryGetQuantityCodeId(string code, out int quantityCodeId)
        {
            ThrowIfNotInitialized();
            return _quantityCodeToId.TryGetValue(code, out quantityCodeId);
        }

        public ValueTask EnsureInitialized() => _initializationOperation.EnsureInitialized();

        private async Task Initialize()
        {
            if (!_schemaInformation.Current.HasValue)
            {
                _logger.LogError($"The current version of the database is not available. Unable in initialize {nameof(SqlServerFhirModel)}.");
                throw new ServiceUnavailableException();
            }

            var connectionStringBuilder = new SqlConnectionStringBuilder(_configuration.ConnectionString);

            _logger.LogInformation("Initializing {Server} {Database}", connectionStringBuilder.DataSource, connectionStringBuilder.InitialCatalog);

            using (var connection = new SqlConnection(_configuration.ConnectionString))
            {
                await connection.OpenAsync();

                using (SqlCommand sqlCommand = connection.CreateCommand())
                {
                    sqlCommand.CommandText = @"
                        SET XACT_ABORT ON
                        BEGIN TRANSACTION

                        INSERT INTO dbo.ResourceType (Name) 
                        SELECT value FROM string_split(@resourceTypes, ',')
                        EXCEPT SELECT Name FROM dbo.ResourceType WITH (TABLOCKX); 

                        -- result set 1
                        SELECT ResourceTypeId, Name FROM dbo.ResourceType;

                        INSERT INTO dbo.SearchParam (Uri)
                        SELECT * FROM  OPENJSON (@searchParams) 
                        WITH (Uri varchar(128) '$.Uri')
                        EXCEPT SELECT Uri FROM dbo.SearchParam;

                        -- result set 2
                        SELECT Uri, SearchParamId FROM dbo.SearchParam;

                        INSERT INTO dbo.ClaimType (Name) 
                        SELECT value FROM string_split(@claimTypes, ',')
                        EXCEPT SELECT Name FROM dbo.ClaimType; 

                        -- result set 3
                        SELECT ClaimTypeId, Name FROM dbo.ClaimType;

                        INSERT INTO dbo.CompartmentType (Name) 
                        SELECT value FROM string_split(@compartmentTypes, ',')
                        EXCEPT SELECT Name FROM dbo.CompartmentType; 

                        -- result set 4
                        SELECT CompartmentTypeId, Name FROM dbo.CompartmentType;
                        
                        COMMIT TRANSACTION
    
                        -- result set 5
                        SELECT Value, SystemId from dbo.System;

                        -- result set 6
                        SELECT Value, QuantityCodeId FROM dbo.QuantityCode";

                    string commaSeparatedResourceTypes = string.Join(",", ModelInfoProvider.GetResourceTypeNames());
                    string searchParametersJson = JsonConvert.SerializeObject(_searchParameterDefinitionManager.AllSearchParameters.Select(p => new { Name = p.Name, Uri = p.Url }));
                    string commaSeparatedClaimTypes = string.Join(',', _securityConfiguration.PrincipalClaims);
                    string commaSeparatedCompartmentTypes = string.Join(',', ModelInfoProvider.GetCompartmentTypeNames());

                    sqlCommand.Parameters.AddWithValue("@resourceTypes", commaSeparatedResourceTypes);
                    sqlCommand.Parameters.AddWithValue("@searchParams", searchParametersJson);
                    sqlCommand.Parameters.AddWithValue("@claimTypes", commaSeparatedClaimTypes);
                    sqlCommand.Parameters.AddWithValue("@compartmentTypes", commaSeparatedCompartmentTypes);

                    using (SqlDataReader reader = await sqlCommand.ExecuteReaderAsync(CommandBehavior.SequentialAccess))
                    {
                        var resourceTypeToId = new Dictionary<string, short>(StringComparer.Ordinal);
                        var resourceTypeIdToTypeName = new Dictionary<short, string>();
                        var searchParamUriToId = new Dictionary<Uri, short>();
                        var systemToId = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                        var quantityCodeToId = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                        var claimNameToId = new Dictionary<string, byte>(StringComparer.Ordinal);
                        var compartmentTypeToId = new Dictionary<string, byte>();

                        // result set 1
                        while (reader.Read())
                        {
                            (short id, string resourceTypeName) = reader.ReadRow(V1.ResourceType.ResourceTypeId, V1.ResourceType.Name);

                            resourceTypeToId.Add(resourceTypeName, id);
                            resourceTypeIdToTypeName.Add(id, resourceTypeName);
                        }

                        // result set 2
                        reader.NextResult();

                        while (reader.Read())
                        {
                            (string uri, short searchParamId) = reader.ReadRow(V1.SearchParam.Uri, V1.SearchParam.SearchParamId);
                            searchParamUriToId.Add(new Uri(uri), searchParamId);
                        }

                        // result set 3
                        reader.NextResult();

                        while (reader.Read())
                        {
                            (byte id, string claimTypeName) = reader.ReadRow(V1.ClaimType.ClaimTypeId, V1.ClaimType.Name);
                            claimNameToId.Add(claimTypeName, id);
                        }

                        // result set 4
                        reader.NextResult();

                        while (reader.Read())
                        {
                            (byte id, string compartmentName) = reader.ReadRow(V1.CompartmentType.CompartmentTypeId, V1.CompartmentType.Name);
                            compartmentTypeToId.Add(compartmentName, id);
                        }

                        // result set 5
                        reader.NextResult();

                        while (reader.Read())
                        {
                            var (value, systemId) = reader.ReadRow(V1.System.Value, V1.System.SystemId);
                            systemToId.TryAdd(value, systemId);
                        }

                        // result set 6
                        reader.NextResult();

                        while (reader.Read())
                        {
                            (string value, int quantityCodeId) = reader.ReadRow(V1.QuantityCode.Value, V1.QuantityCode.QuantityCodeId);
                            quantityCodeToId.TryAdd(value, quantityCodeId);
                        }

                        _resourceTypeToId = resourceTypeToId;
                        _resourceTypeIdToTypeName = resourceTypeIdToTypeName;
                        _searchParamUriToId = searchParamUriToId;
                        _systemToId = systemToId;
                        _quantityCodeToId = quantityCodeToId;
                        _claimNameToId = claimNameToId;
                        _compartmentTypeToId = compartmentTypeToId;
                    }
                }
            }
        }

        private int GetStringId(ConcurrentDictionary<string, int> cache, string stringValue, Table table, Column<int> idColumn, Column<string> stringColumn)
        {
            if (cache.TryGetValue(stringValue, out int id))
            {
                return id;
            }

            _logger.LogInformation("Cache miss for string ID on {table}", table);

            using (var connection = new SqlConnection(_configuration.ConnectionString))
            {
                connection.Open();

                using (SqlCommand sqlCommand = connection.CreateCommand())
                {
                    sqlCommand.CommandText = $@"
                        SET TRANSACTION ISOLATION LEVEL SERIALIZABLE
                        BEGIN TRANSACTION

                        DECLARE @id int = (SELECT {idColumn} FROM {table} WITH (UPDLOCK) WHERE {stringColumn} = @stringValue)

                        IF (@id IS NULL) BEGIN
                            INSERT INTO {table} 
                                ({stringColumn})
                            VALUES 
                                (@stringValue)
                            SET @id = SCOPE_IDENTITY()
                        END

                        COMMIT TRANSACTION

                        SELECT @id";

                    sqlCommand.Parameters.AddWithValue("@stringValue", stringValue);

                    id = (int)sqlCommand.ExecuteScalar();

                    cache.TryAdd(stringValue, id);
                    return id;
                }
            }
        }

        private void ThrowIfNotInitialized()
        {
            if (!_initializationOperation.IsInitialized)
            {
                _logger.LogError($"The {nameof(SqlServerFhirModel)} instance has not been initialized.");
                throw new ServiceUnavailableException();
            }
        }

        public void Dispose()
        {
            _initializationOperation?.Dispose();
        }
    }
}
