﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Abstractions.Exceptions;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Messages.Storage;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.Fhir.SqlServer.Features.Storage.Registry;
using Microsoft.Health.SqlServer;
using Microsoft.Health.SqlServer.Features.Schema;
using Microsoft.Health.SqlServer.Features.Schema.Model;
using Microsoft.Health.SqlServer.Features.Storage;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    /// <summary>
    /// Maintains IDs for resource types, search parameters, systems, and codes for quantity search parameters.
    /// There are typically on the order of tens or hundreds of distinct values for each of these, but are reused
    /// many many times in the database. For more compact storage, we use IDs instead of the strings when referencing these.
    /// Also, because the number of distinct values is small, we can maintain all values in memory and avoid joins when querying.
    /// </summary>
    public sealed class SqlServerFhirModel : IRequireInitializationOnFirstRequest, ISqlServerFhirModel
    {
        private readonly SchemaInformation _schemaInformation;
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager;
        private readonly ISearchParameterStatusDataStore _filebasedSearchParameterStatusDataStore;
        private readonly SecurityConfiguration _securityConfiguration;
        private readonly ISqlConnectionStringProvider _sqlConnectionStringProvider;
        private readonly IMediator _mediator;
        private readonly ILogger<SqlServerFhirModel> _logger;
        private Dictionary<string, short> _resourceTypeToId;
        private Dictionary<short, string> _resourceTypeIdToTypeName;
        private Dictionary<Uri, short> _searchParamUriToId;
        private ConcurrentDictionary<string, int> _systemToId;
        private ConcurrentDictionary<string, int> _quantityCodeToId;
        private Dictionary<string, byte> _claimNameToId;
        private Dictionary<string, byte> _compartmentTypeToId;
        private int _highestInitializedVersion;

        private (short lowestId, short highestId) _resourceTypeIdRange;

        public SqlServerFhirModel(
            SchemaInformation schemaInformation,
            ISearchParameterDefinitionManager searchParameterDefinitionManager,
            FilebasedSearchParameterStatusDataStore.Resolver filebasedRegistry,
            IOptions<SecurityConfiguration> securityConfiguration,
            ISqlConnectionStringProvider sqlConnectionStringProvider,
            IMediator mediator,
            ILogger<SqlServerFhirModel> logger)
        {
            EnsureArg.IsNotNull(schemaInformation, nameof(schemaInformation));
            EnsureArg.IsNotNull(searchParameterDefinitionManager, nameof(searchParameterDefinitionManager));
            EnsureArg.IsNotNull(filebasedRegistry, nameof(filebasedRegistry));
            EnsureArg.IsNotNull(securityConfiguration?.Value, nameof(securityConfiguration));
            EnsureArg.IsNotNull(sqlConnectionStringProvider, nameof(sqlConnectionStringProvider));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _schemaInformation = schemaInformation;
            _searchParameterDefinitionManager = searchParameterDefinitionManager;
            _filebasedSearchParameterStatusDataStore = filebasedRegistry.Invoke();
            _securityConfiguration = securityConfiguration.Value;
            _sqlConnectionStringProvider = sqlConnectionStringProvider;
            _mediator = mediator;
            _logger = logger;
        }

        public (short lowestId, short highestId) ResourceTypeIdRange
        {
            get
            {
                ThrowIfNotInitialized();
                return _resourceTypeIdRange;
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

        public void TryAddSearchParamIdToUriMapping(string searchParamUri, short searchParamId)
        {
            ThrowIfNotInitialized();

            _searchParamUriToId.TryAdd(new Uri(searchParamUri), searchParamId);
        }

        public void RemoveSearchParamIdToUriMapping(string searchParamUri)
        {
            ThrowIfNotInitialized();

            _searchParamUriToId.Remove(new Uri(searchParamUri));
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

            VLatest.SystemTable systemTable = VLatest.System;
            return GetStringId(_systemToId, system, systemTable, systemTable.SystemId, systemTable.Value);
        }

        public int GetQuantityCodeId(string code)
        {
            ThrowIfNotInitialized();

            VLatest.QuantityCodeTable quantityCodeTable = VLatest.QuantityCode;
            return GetStringId(_quantityCodeToId, code, quantityCodeTable, quantityCodeTable.QuantityCodeId, quantityCodeTable.Value);
        }

        public bool TryGetQuantityCodeId(string code, out int quantityCodeId)
        {
            ThrowIfNotInitialized();
            return _quantityCodeToId.TryGetValue(code, out quantityCodeId);
        }

        public async Task EnsureInitialized()
        {
            ThrowIfCurrentSchemaVersionIsNull();

            // If the fhir-server is just starting up, synchronize the fhir-server dictionaries with the SQL database
            await Initialize((int)_schemaInformation.Current, true, CancellationToken.None);
        }

        public async Task Initialize(int version, bool runAllInitialization, CancellationToken cancellationToken)
        {
            // This also covers the scenario when database is not setup so _highestInitializedVersion and version is 0.
            if (_highestInitializedVersion == version)
            {
                return;
            }

            var connectionStringBuilder = new SqlConnectionStringBuilder(await _sqlConnectionStringProvider.GetSqlConnectionString(cancellationToken));
            _logger.LogInformation("Initializing {Server} {Database} to version {Version}", connectionStringBuilder.DataSource, connectionStringBuilder.InitialCatalog, version);

            // If we are applying a full snap shot schema file, or if the server is just starting up
            if (runAllInitialization || _highestInitializedVersion == 0)
            {
                // Run the schema initialization required for all schema versions, from the minimum version to the current version.
                await InitializeBase(cancellationToken);

                if (version >= SchemaVersionConstants.SearchParameterStatusSchemaVersion)
                {
                    await InitializeSearchParameterStatuses(cancellationToken);
                }
            }
            else
            {
                // Only run the schema initialization required for the current version
                if (version == SchemaVersionConstants.SearchParameterStatusSchemaVersion)
                {
                    await InitializeSearchParameterStatuses(cancellationToken);
                }
            }

            _highestInitializedVersion = version;

            await _mediator.Publish(new StorageInitializedNotification(), CancellationToken.None);
        }

        private async Task InitializeBase(CancellationToken cancellationToken)
        {
            using (var connection = new SqlConnection(await _sqlConnectionStringProvider.GetSqlConnectionString(cancellationToken)))
            {
                connection.Open();

                // Synchronous calls are used because this code is executed on startup and doesn't need to be async.
                // Additionally, XUnit task scheduler constraints prevent async calls from being easily tested.
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

                    string searchParametersJson = JsonConvert.SerializeObject(_searchParameterDefinitionManager.AllSearchParameters.Select(p => new { Uri = p.Url }));
                    string commaSeparatedResourceTypes = string.Join(",", ModelInfoProvider.GetResourceTypeNames());
                    string commaSeparatedClaimTypes = string.Join(',', _securityConfiguration.PrincipalClaims);
                    string commaSeparatedCompartmentTypes = string.Join(',', ModelInfoProvider.GetCompartmentTypeNames());

                    sqlCommand.Parameters.AddWithValue("@searchParams", searchParametersJson);
                    sqlCommand.Parameters.AddWithValue("@resourceTypes", commaSeparatedResourceTypes);
                    sqlCommand.Parameters.AddWithValue("@claimTypes", commaSeparatedClaimTypes);
                    sqlCommand.Parameters.AddWithValue("@compartmentTypes", commaSeparatedCompartmentTypes);

                    using (SqlDataReader reader = sqlCommand.ExecuteReader(CommandBehavior.SequentialAccess))
                    {
                        var resourceTypeToId = new Dictionary<string, short>(StringComparer.Ordinal);
                        var resourceTypeIdToTypeName = new Dictionary<short, string>();
                        var searchParamUriToId = new Dictionary<Uri, short>();
                        var systemToId = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                        var quantityCodeToId = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                        var claimNameToId = new Dictionary<string, byte>(StringComparer.Ordinal);
                        var compartmentTypeToId = new Dictionary<string, byte>();

                        // result set 1
                        short lowestResourceTypeId = short.MaxValue;
                        short highestResourceTypeId = short.MinValue;
                        while (reader.Read())
                        {
                            (short id, string resourceTypeName) = reader.ReadRow(VLatest.ResourceType.ResourceTypeId, VLatest.ResourceType.Name);

                            resourceTypeToId.Add(resourceTypeName, id);
                            if (id > highestResourceTypeId)
                            {
                                highestResourceTypeId = id;
                            }

                            if (id < lowestResourceTypeId)
                            {
                                lowestResourceTypeId = id;
                            }

                            resourceTypeIdToTypeName.Add(id, resourceTypeName);
                        }

                        // result set 2
                        reader.NextResult();

                        while (reader.Read())
                        {
                            (string uri, short searchParamId) = reader.ReadRow(VLatest.SearchParam.Uri, VLatest.SearchParam.SearchParamId);
                            searchParamUriToId.Add(new Uri(uri), searchParamId);
                        }

                        // result set 3
                        reader.NextResult();

                        while (reader.Read())
                        {
                            (byte id, string claimTypeName) = reader.ReadRow(VLatest.ClaimType.ClaimTypeId, VLatest.ClaimType.Name);
                            claimNameToId.Add(claimTypeName, id);
                        }

                        // result set 4
                        reader.NextResult();

                        while (reader.Read())
                        {
                            (byte id, string compartmentName) = reader.ReadRow(VLatest.CompartmentType.CompartmentTypeId, VLatest.CompartmentType.Name);
                            compartmentTypeToId.Add(compartmentName, id);
                        }

                        // result set 5
                        reader.NextResult();

                        while (reader.Read())
                        {
                            var (value, systemId) = reader.ReadRow(VLatest.System.Value, VLatest.System.SystemId);
                            systemToId.TryAdd(value, systemId);
                        }

                        // result set 6
                        reader.NextResult();

                        while (reader.Read())
                        {
                            (string value, int quantityCodeId) = reader.ReadRow(VLatest.QuantityCode.Value, VLatest.QuantityCode.QuantityCodeId);
                            quantityCodeToId.TryAdd(value, quantityCodeId);
                        }

                        _resourceTypeToId = resourceTypeToId;
                        _resourceTypeIdToTypeName = resourceTypeIdToTypeName;
                        _searchParamUriToId = searchParamUriToId;
                        _systemToId = systemToId;
                        _quantityCodeToId = quantityCodeToId;
                        _claimNameToId = claimNameToId;
                        _compartmentTypeToId = compartmentTypeToId;
                        _resourceTypeIdRange = (lowestResourceTypeId, highestResourceTypeId);
                    }
                }
            }
        }

        private async Task InitializeSearchParameterStatuses(CancellationToken cancellationToken)
        {
            using (var connection = new SqlConnection(await _sqlConnectionStringProvider.GetSqlConnectionString(cancellationToken)))
            {
                connection.Open();

                using (SqlCommand sqlCommand = connection.CreateCommand())
                {
                    sqlCommand.CommandText = @"
                        SET XACT_ABORT ON
                        BEGIN TRANSACTION
                        DECLARE @lastUpdated datetimeoffset(7) = SYSDATETIMEOFFSET()
    
                        UPDATE dbo.SearchParam
                        SET Status = sps.Status, LastUpdated = @lastUpdated, IsPartiallySupported = sps.IsPartiallySupported
                        FROM dbo.SearchParam INNER JOIN @searchParamStatuses as sps
                        ON dbo.SearchParam.Uri = sps.Uri
                        COMMIT TRANSACTION";

                    IEnumerable<ResourceSearchParameterStatus> statuses = _filebasedSearchParameterStatusDataStore
                        .GetSearchParameterStatuses(cancellationToken).GetAwaiter().GetResult();

                    var collection = new SearchParameterStatusCollection();
                    collection.AddRange(statuses);

                    var tableValuedParameter = new SqlParameter
                    {
                        ParameterName = "searchParamStatuses",
                        SqlDbType = SqlDbType.Structured,
                        Value = collection,
                        Direction = ParameterDirection.Input,
                        TypeName = "dbo.SearchParamTableType_1",
                    };

                    sqlCommand.Parameters.Add(tableValuedParameter);
                    sqlCommand.ExecuteNonQuery();
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

            using (var connection = new SqlConnection(_sqlConnectionStringProvider.GetSqlConnectionString(CancellationToken.None).GetAwaiter().GetResult()))
            {
                connection.Open();

                using (SqlCommand sqlCommand = connection.CreateCommand())
                {
                    // This command are not using any user arguments, and can't be rewritten to parametrized command string
                    // because you can't parameterize column or table.
#pragma warning disable CA2100
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

#pragma warning restore CA2100
                    id = (int)sqlCommand.ExecuteScalar();

                    cache.TryAdd(stringValue, id);
                    return id;
                }
            }
        }

        private void ThrowIfNotInitialized()
        {
            ThrowIfCurrentSchemaVersionIsNull();

            if (_highestInitializedVersion < _schemaInformation.Current)
            {
                _logger.LogError($"The {nameof(SqlServerFhirModel)} instance has not run the initialization required for the current schema version");
                throw new ServiceUnavailableException();
            }
        }

        private void ThrowIfCurrentSchemaVersionIsNull()
        {
            if (_schemaInformation.Current == null)
            {
                throw new InvalidOperationException(Resources.SchemaVersionShouldNotBeNull);
            }
        }
    }
}
