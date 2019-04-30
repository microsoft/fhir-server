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
using EnsureThat;
using Hl7.Fhir.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.SqlServer.Configs;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    public class SqlServerFhirModel
    {
        private readonly SqlServerDataStoreConfiguration _configuration;
        private readonly ILogger<SqlServerFhirDataStore> _logger;
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager;
        private readonly Dictionary<string, short> _resourceTypeToId = new Dictionary<string, short>(StringComparer.Ordinal);
        private readonly Dictionary<short, string> _resourceTypeIdToTypeName = new Dictionary<short, string>();
        private readonly Dictionary<string, short> _searchParamUriToId = new Dictionary<string, short>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, int> _systemToId = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, int> _quantityCodeToId = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public SqlServerFhirModel(SqlServerDataStoreConfiguration configuration, ISearchParameterDefinitionManager searchParameterDefinitionManager, ILogger<SqlServerFhirDataStore> logger)
        {
            EnsureArg.IsNotNull(configuration, nameof(configuration));
            EnsureArg.IsNotNull(logger, nameof(logger));
            _configuration = configuration;
            _logger = logger;
            _searchParameterDefinitionManager = searchParameterDefinitionManager;
            Initialize();
        }

        public short GetResourceTypeId(string resourceTypeName)
        {
            return _resourceTypeToId[resourceTypeName];
        }

        public short GetSearchParamId(string searchParamUri)
        {
            return _searchParamUriToId[searchParamUri];
        }

        public int GetSystem(string system)
        {
            return GetStringId(_systemToId, system, "System", "SystemId", "System");
        }

        public int GetQuantityCode(string code)
        {
            return GetStringId(_quantityCodeToId, code, "QuantityCode", "QuantityCodeId", "QuantityCode");
        }

        private void Initialize()
        {
            using (var connection = new SqlConnection(_configuration.ConnectionString))
            {
                connection.Open();

                using (SqlCommand sqlCommand = connection.CreateCommand())
                {
                    sqlCommand.CommandText = @"
                        SET XACT_ABORT ON
                        BEGIN TRANSACTION

                        INSERT INTO ResourceType (Name) 
                        SELECT value FROM string_split(@resourceTypes, ',')
                        EXCEPT SELECT Name FROM ResourceType WITH (TABLOCKX); 

                        SELECT ResourceTypeId, Name FROM ResourceType;

                        INSERT INTO SearchParam (Uri)
                        SELECT * FROM  OPENJSON (@searchParams) 
                        WITH (Uri varchar(128) '$.Uri')
                        EXCEPT SELECT Uri from SearchParam;

                        SELECT Uri, SearchParamId FROM SearchParam;

                        COMMIT TRANSACTION

                        SELECT System, SystemId from System;

                        SELECT QuantityCode, QuantityCodeId from QuantityCode";

                    string commaSeparatedResourceTypes = string.Join(",", ModelInfo.SupportedResources);
                    string searchParametersJson = JsonConvert.SerializeObject(_searchParameterDefinitionManager.AllSearchParameters.Select(p => new { Name = p.Name, Uri = p.Url }));

                    sqlCommand.Parameters.AddWithValue("@resourceTypes", commaSeparatedResourceTypes);
                    sqlCommand.Parameters.AddWithValue("@searchParams", searchParametersJson);

                    using (SqlDataReader reader = sqlCommand.ExecuteReader(CommandBehavior.SequentialAccess))
                    {
                        while (reader.Read())
                        {
                            short id = reader.GetInt16(0);
                            string resourceTypeName = reader.GetString(1);
                            _resourceTypeToId.Add(resourceTypeName, id);
                            _resourceTypeIdToTypeName.Add(id, resourceTypeName);
                        }

                        reader.NextResult();

                        while (reader.Read())
                        {
                            _searchParamUriToId.Add(reader.GetString(0), reader.GetInt16(1));
                        }

                        reader.NextResult();

                        while (reader.Read())
                        {
                            _systemToId.TryAdd(reader.GetString(0), reader.GetInt32(1));
                        }

                        reader.NextResult();

                        while (reader.Read())
                        {
                            _quantityCodeToId.TryAdd(reader.GetString(0), reader.GetInt32(1));
                        }
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

            using (var connection = new SqlConnection(_configuration.ConnectionString))
            {
                connection.Open();

                using (SqlCommand sqlCommand = connection.CreateCommand())
                {
                    sqlCommand.CommandText = $@"
                        SET TRANSACTION ISOLATION LEVEL SERIALIZABLE
                        BEGIN TRAN

                        DECLARE @id int = (SELECT {idColumnName} FROM {tableName} WITH (UPDLOCK) WHERE {stringColumnName} = @stringValue)

                        IF @id IS NULL
                        BEGIN
                            INSERT INTO {tableName} ({stringColumnName})
                            VALUES (@stringValue)
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
    }
}
