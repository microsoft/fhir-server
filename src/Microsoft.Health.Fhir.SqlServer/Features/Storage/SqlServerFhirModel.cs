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
using Hl7.Fhir.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.SqlServer.Configs;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
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
        private Dictionary<string, short> _resourceTypeToId;
        private Dictionary<short, string> _resourceTypeIdToTypeName;
        private Dictionary<string, short> _searchParamUriToId;
        private ConcurrentDictionary<string, int> _systemToId;
        private ConcurrentDictionary<string, int> _quantityCodeToId;

        private readonly RetryableInitializationOperation _initializationOperation;

        public SqlServerFhirModel(SqlServerDataStoreConfiguration configuration, SchemaInformation schemaInformation, ISearchParameterDefinitionManager searchParameterDefinitionManager, ILogger<SqlServerFhirModel> logger)
        {
            EnsureArg.IsNotNull(configuration, nameof(configuration));
            EnsureArg.IsNotNull(schemaInformation, nameof(schemaInformation));
            EnsureArg.IsNotNull(searchParameterDefinitionManager, nameof(searchParameterDefinitionManager));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _configuration = configuration;
            _schemaInformation = schemaInformation;
            _logger = logger;
            _searchParameterDefinitionManager = searchParameterDefinitionManager;

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

        public short GetSearchParamId(string searchParamUri)
        {
            ThrowIfNotInitialized();
            return _searchParamUriToId[searchParamUri];
        }

        public int GetSystem(string system)
        {
            ThrowIfNotInitialized();
            return GetStringId(_systemToId, system, "dbo.System", "SystemId", "System");
        }

        public int GetQuantityCode(string code)
        {
            ThrowIfNotInitialized();
            return GetStringId(_quantityCodeToId, code, "dbo.QuantityCode", "QuantityCodeId", "QuantityCode");
        }

        public ValueTask EnsureInitialized() => _initializationOperation.EnsureInitialized();

        private async Task Initialize()
        {
            if (!_schemaInformation.Current.HasValue)
            {
                _logger.LogError($"The current version of the database is not available. Unable in initialize {nameof(SqlServerFhirModel)}.");
                throw new ServiceUnavailableException();
            }

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

                        INSERT INTO SearchParam (Uri)
                        SELECT * FROM  OPENJSON (@searchParams) 
                        WITH (Uri varchar(128) '$.Uri')
                        EXCEPT SELECT Uri FROM dbo.SearchParam;

                        -- result set 2
                        SELECT Uri, SearchParamId FROM dbo.SearchParam;

                        COMMIT TRANSACTION
    
                        -- result set 3
                        SELECT System, SystemId from dbo.System;

                        -- result set 4
                        SELECT QuantityCode, QuantityCodeId FROM dbo.QuantityCode";

                    string commaSeparatedResourceTypes = string.Join(",", ModelInfo.SupportedResources);
                    string searchParametersJson = JsonConvert.SerializeObject(_searchParameterDefinitionManager.AllSearchParameters.Select(p => new { Name = p.Name, Uri = p.Url }));

                    sqlCommand.Parameters.AddWithValue("@resourceTypes", commaSeparatedResourceTypes);
                    sqlCommand.Parameters.AddWithValue("@searchParams", searchParametersJson);

                    using (SqlDataReader reader = await sqlCommand.ExecuteReaderAsync(CommandBehavior.SequentialAccess))
                    {
                        var resourceTypeToId = new Dictionary<string, short>(StringComparer.Ordinal);
                        var resourceTypeIdToTypeName = new Dictionary<short, string>();
                        var searchParamUriToId = new Dictionary<string, short>(StringComparer.Ordinal);
                        var systemToId = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                        var quantityCodeToId = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                        // result set 1
                        while (reader.Read())
                        {
                            short id = reader.GetInt16("ResourceTypeId", 0);
                            string resourceTypeName = reader.GetString("Name", 1);

                            resourceTypeToId.Add(resourceTypeName, id);
                            resourceTypeIdToTypeName.Add(id, resourceTypeName);
                        }

                        // result set 2
                        reader.NextResult();

                        while (reader.Read())
                        {
                            searchParamUriToId.Add(
                                reader.GetString("Uri", 0),
                                reader.GetInt16("SearchParamId", 1));
                        }

                        // result set 3
                        reader.NextResult();

                        while (reader.Read())
                        {
                            systemToId.TryAdd(
                                reader.GetString("System", 0),
                                reader.GetInt32("SystemId", 1));
                        }

                        // result set 4
                        reader.NextResult();

                        while (reader.Read())
                        {
                            quantityCodeToId.TryAdd(
                                reader.GetString("QuantityCode", 0),
                                reader.GetInt32("QuantityCodeId", 1));
                        }

                        _resourceTypeToId = resourceTypeToId;
                        _resourceTypeIdToTypeName = resourceTypeIdToTypeName;
                        _searchParamUriToId = searchParamUriToId;
                        _systemToId = systemToId;
                        _quantityCodeToId = quantityCodeToId;
                    }
                }
            }
        }

        private int GetStringId(ConcurrentDictionary<string, int> cache, string stringValue, string tableName, string idColumnName, string stringColumnName)
        {
            if (cache.TryGetValue(stringValue, out int id))
            {
                return id;
            }

            _logger.LogInformation("Cache miss for string ID on {table}", tableName);

            using (var connection = new SqlConnection(_configuration.ConnectionString))
            {
                connection.Open();

                using (SqlCommand sqlCommand = connection.CreateCommand())
                {
                    sqlCommand.CommandText = $@"
                        SET TRANSACTION ISOLATION LEVEL SERIALIZABLE
                        BEGIN TRAN

                        DECLARE @id int = (SELECT {idColumnName} FROM {tableName} WITH (UPDLOCK) WHERE {stringColumnName} = @stringValue)

                        IF (@id IS NULL) BEGIN
                            INSERT INTO {tableName} 
                                ({stringColumnName})
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
