// -------------------------------------------------------------------------------------------------
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
using Hl7.Fhir.Model;
using MediatR;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Abstractions.Exceptions;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Features.Storage;
using Microsoft.Health.Fhir.Core.Messages.Storage;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.Fhir.SqlServer.Features.Storage.Registry;
using Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration;
using Microsoft.Health.SqlServer.Features.Client;
using Microsoft.Health.SqlServer.Features.Schema;
using Microsoft.Health.SqlServer.Features.Schema.Model;
using Microsoft.Health.SqlServer.Features.Storage;
using Namotion.Reflection;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    /// <summary>
    /// Maintains IDs for resource types, search parameters, systems, and codes for quantity search parameters.
    /// There are typically on the order of tens or hundreds of distinct values for each of these, but are reused
    /// many many times in the database. For more compact storage, we use IDs instead of the strings when referencing these.
    /// Also, because the number of distinct values is small, we can maintain all values in memory and avoid joins when querying.
    /// </summary>
#pragma warning disable CA1001 // Types that own disposable fields should be disposable. Justification: SQLServerFhirModel is maintained in memory during all process execution.
    public sealed class SqlServerFhirModel : ISqlServerFhirModel
#pragma warning restore CA1001
    {
        private readonly SchemaInformation _schemaInformation;
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager;
        private readonly ISearchParameterStatusDataStore _filebasedSearchParameterStatusDataStore;
        private readonly SecurityConfiguration _securityConfiguration;
        private readonly IScopeProvider<SqlConnectionWrapperFactory> _scopedSqlConnectionWrapperFactory;
        private readonly IMediator _mediator;
        private readonly ISqlRetryService _sqlRetryService;
        private readonly ILogger<SqlServerFhirModel> _logger;
        private Dictionary<string, short> _resourceTypeToId;
        private Dictionary<short, string> _resourceTypeIdToTypeName;
        private Dictionary<Uri, short> _searchParamUriToId;
        private FhirMemoryCache<int> _systemToId;
        private FhirMemoryCache<int> _quantityCodeToId;
        private Dictionary<string, byte> _claimNameToId;
        private Dictionary<string, byte> _compartmentTypeToId;
        private int _highestInitializedVersion;

        private (short lowestId, short highestId) _resourceTypeIdRange;

        public SqlServerFhirModel(
            SchemaInformation schemaInformation,
            ISearchParameterDefinitionManager searchParameterDefinitionManager,
            FilebasedSearchParameterStatusDataStore.Resolver filebasedRegistry,
            IOptions<SecurityConfiguration> securityConfiguration,
            IScopeProvider<SqlConnectionWrapperFactory> scopedSqlConnectionWrapperFactory,
            IMediator mediator,
            ISqlRetryService sqlRetryService,
            ILogger<SqlServerFhirModel> logger)
        {
            EnsureArg.IsNotNull(schemaInformation, nameof(schemaInformation));
            EnsureArg.IsNotNull(searchParameterDefinitionManager, nameof(searchParameterDefinitionManager));
            EnsureArg.IsNotNull(filebasedRegistry, nameof(filebasedRegistry));
            EnsureArg.IsNotNull(securityConfiguration?.Value, nameof(securityConfiguration));
            EnsureArg.IsNotNull(scopedSqlConnectionWrapperFactory, nameof(scopedSqlConnectionWrapperFactory));
            EnsureArg.IsNotNull(sqlRetryService, nameof(sqlRetryService));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _schemaInformation = schemaInformation;
            _searchParameterDefinitionManager = searchParameterDefinitionManager;
            _filebasedSearchParameterStatusDataStore = filebasedRegistry.Invoke();
            _securityConfiguration = securityConfiguration.Value;
            _scopedSqlConnectionWrapperFactory = scopedSqlConnectionWrapperFactory;
            _mediator = mediator;
            _sqlRetryService = sqlRetryService;
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

            if (_resourceTypeToId.TryGetValue(resourceTypeName, out short resourceTypeId))
            {
                return resourceTypeId;
            }

            throw new ResourceNotFoundException($"Resource type '{resourceTypeName}' is not a known resource type.");
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

        public bool TryGetSearchParamId(Uri searchParamUri, out short id)
        {
            ThrowIfNotInitialized();
            return _searchParamUriToId.TryGetValue(searchParamUri, out id);
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
            return _systemToId.TryGet(system, out systemId);
        }

        public int GetSystemId(string system)
        {
            ThrowIfNotInitialized();

            var systemSproc = VLatest.GetSystemId;
            return GetStringId(_systemToId, system, systemSproc);
        }

        public int GetQuantityCodeId(string code)
        {
            ThrowIfNotInitialized();

            var quantityCodeSproc = VLatest.GetQuantityCodeId;
            return GetStringId(_quantityCodeToId, code, quantityCodeSproc);
        }

        public bool TryGetQuantityCodeId(string code, out int quantityCodeId)
        {
            ThrowIfNotInitialized();
            return _quantityCodeToId.TryGet(code, out quantityCodeId);
        }

        public async Task EnsureInitialized()
        {
            ThrowIfCurrentSchemaVersionIsNull();

            // If the fhir-server is just starting up, synchronize the fhir-server dictionaries with the SQL database
            await Initialize((int)_schemaInformation.Current, CancellationToken.None);
        }

        public async Task Initialize(int version, CancellationToken cancellationToken)
        {
            // This also covers the scenario when database is not setup so _highestInitializedVersion and version is 0.
            if (_highestInitializedVersion == version)
            {
                return;
            }

            using (IScoped<SqlConnectionWrapperFactory> scopedSqlConnectionWrapperFactory = _scopedSqlConnectionWrapperFactory.Invoke())
            using (SqlConnectionWrapper sqlConnectionWrapper = await scopedSqlConnectionWrapperFactory.Value.ObtainSqlConnectionWrapperAsync(cancellationToken, true))
            using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateRetrySqlCommand())
            {
                _logger.LogInformation("Initializing {Server} {Database} to version {Version}", sqlCommandWrapper.Connection.DataSource, sqlCommandWrapper.Connection.Database, version);
            }

            // Run the schema initialization required for all schema versions, from the minimum version to the current version.
            await InitializeBase(cancellationToken);

            // If we are applying a full snap shot schema file, or if the server is just starting up
            await InitializeSearchParameterStatuses(cancellationToken);

            _highestInitializedVersion = version;

            await _mediator.Publish(new StorageInitializedNotification(), CancellationToken.None);
        }

        private async Task InitializeBase(CancellationToken cancellationToken)
        {
            if (_schemaInformation.Current < SchemaVersionConstants.FhirModelInitialization)
            {
                await LegacyInitializeBase(cancellationToken);
                return;
            }

            string searchParametersJson = JsonConvert.SerializeObject(_searchParameterDefinitionManager.AllSearchParameters.Select(p => new { Uri = p.Url, IsPartiallySupported = p.IsPartiallySupported }));
            string commaSeparatedResourceTypes = string.Join(",", ModelInfoProvider.GetResourceTypeNames());
            string commaSeparatedClaimTypes = string.Join(',', _securityConfiguration.PrincipalClaims);
            string commaSeparatedCompartmentTypes = string.Join(',', ModelInfoProvider.GetCompartmentTypeNames());

            using var cmd = new SqlCommand();
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = "dbo.InitializeBase";
            cmd.Parameters.AddWithValue("@searchParams", searchParametersJson);
            cmd.Parameters.AddWithValue("@resourceTypes", commaSeparatedResourceTypes);
            cmd.Parameters.AddWithValue("@claimTypes", commaSeparatedClaimTypes);
            cmd.Parameters.AddWithValue("@compartmentTypes", commaSeparatedCompartmentTypes);

            // The stored procedure returns 6 result sets, so the reader needs to handle 6 sets of results.
            var resultsList = await cmd.ExecuteMultiResultReaderAsync(
                _sqlRetryService,
                new List<Func<SqlDataReader, object>>()
                {
                    (reader) =>
                    {
                        return reader.ReadRow(VLatest.ResourceType.ResourceTypeId, VLatest.ResourceType.Name);
                    },
                    (reader) =>
                    {
                        return reader.ReadRow(VLatest.SearchParam.Uri, VLatest.SearchParam.SearchParamId);
                    },
                    (reader) =>
                    {
                        return reader.ReadRow(VLatest.ClaimType.ClaimTypeId, VLatest.ClaimType.Name);
                    },
                    (reader) =>
                    {
                        return reader.ReadRow(VLatest.CompartmentType.CompartmentTypeId, VLatest.CompartmentType.Name);
                    },
                    (reader) =>
                    {
                        return reader.ReadRow(VLatest.System.Value, VLatest.System.SystemId);
                    },
                    (reader) =>
                    {
                        return reader.ReadRow(VLatest.QuantityCode.Value, VLatest.QuantityCode.QuantityCodeId);
                    },
                },
                _logger,
                cancellationToken);

            if (resultsList.Count != 6)
            {
                _logger.LogError("Unexpected number of result sets returned from InitializeBase sproc. Expected: 6, Actual: {Count}", resultsList.Count);
                throw new InvalidOperationException($"Unexpected number of result sets returned from InitializeBase sproc. Expected: 6, Actual: {resultsList.Count}");
            }

            var resourceTypeToId = new Dictionary<string, short>(StringComparer.Ordinal);
            var resourceTypeIdToTypeName = new Dictionary<short, string>();
            var searchParamUriToId = new Dictionary<Uri, short>();
            var claimNameToId = new Dictionary<string, byte>(StringComparer.Ordinal);
            var compartmentTypeToId = new Dictionary<string, byte>();

            // result set 1
            short lowestResourceTypeId = short.MaxValue;
            short highestResourceTypeId = short.MinValue;
            foreach (var result in resultsList[0])
            {
                (short id, string resourceTypeName) = ((short, string))result;
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
            foreach (var result in resultsList[1])
            {
                (string uri, short searchParamId) = ((string, short))result;
                searchParamUriToId.Add(new Uri(uri), searchParamId);
            }

            // result set 3
            foreach (var result in resultsList[2])
            {
                (byte id, string claimTypeName) = ((byte, string))result;
                claimNameToId.Add(claimTypeName, id);
            }

            // result set 4
            foreach (var result in resultsList[3])
            {
                (byte id, string compartmentName) = ((byte, string))result;
                compartmentTypeToId.Add(compartmentName, id);
            }

            // result set 5
            _systemToId = new FhirMemoryCache<int>("systemToId", _logger, ignoreCase: true);
            bool systemWarningLogged = false;
            foreach (var result in resultsList[4])
            {
                var (value, systemId) = ((string, int))result;

                if (!_systemToId.TryAdd(value, systemId) && !systemWarningLogged)
                {
                    _logger.LogWarning($"Cache '{_systemToId.Name}' reached the limit of {_systemToId.CacheMemoryLimit} bytes (with {_systemToId.Count} cached elements).");
                    systemWarningLogged = true;
                }
            }

            // result set 6
            _quantityCodeToId = new FhirMemoryCache<int>("quantityCodeToId", _logger, ignoreCase: true);
            bool quantityCodeWarningLogged = false;
            foreach (var result in resultsList[5])
            {
                (string value, int quantityCodeId) = ((string, int))result;
                if (!_quantityCodeToId.TryAdd(value, quantityCodeId) && !quantityCodeWarningLogged)
                {
                    _logger.LogWarning($"Cache '{_quantityCodeToId.Name}' reached the limit of {_quantityCodeToId.CacheMemoryLimit} bytes (with {_quantityCodeToId.Count} cached elements).");
                    quantityCodeWarningLogged = true;
                }
            }

            _resourceTypeToId = resourceTypeToId;
            _resourceTypeIdToTypeName = resourceTypeIdToTypeName;
            _searchParamUriToId = searchParamUriToId;
            _claimNameToId = claimNameToId;
            _compartmentTypeToId = compartmentTypeToId;
            _resourceTypeIdRange = (lowestResourceTypeId, highestResourceTypeId);
        }

        private async Task InitializeSearchParameterStatuses(CancellationToken cancellationToken, int retryDepth = 0)
        {
            if (_schemaInformation.Current < SchemaVersionConstants.FhirModelInitialization)
            {
                await LegacyInitializeSearchParameterStatuses(cancellationToken);
                return;
            }

            _logger.LogInformation("Initializing search parameters statuses.");

            List<ResourceSearchParameterStatus> fileStatuses = (await _filebasedSearchParameterStatusDataStore
                    .GetSearchParameterStatuses(cancellationToken)).ToList();

            using var getStatusCmd = new SqlCommand();
            getStatusCmd.CommandType = CommandType.StoredProcedure;
            getStatusCmd.CommandText = "dbo.GetSearchParamStatuses";

            var existingParams = await getStatusCmd.ExecuteReaderAsync(
                _sqlRetryService,
                (reader) =>
                {
                    (_, var uri, var stringStatus, var lastUpdated, var isPartiallySupported) = reader.ReadRow(
                            VLatest.SearchParam.SearchParamId,
                            VLatest.SearchParam.Uri,
                            VLatest.SearchParam.Status,
                            VLatest.SearchParam.LastUpdated,
                            VLatest.SearchParam.IsPartiallySupported);
                    return new ResourceSearchParameterStatus
                    {
                        Uri = new Uri(uri),
                        Status = Enum.Parse<SearchParameterStatus>(stringStatus),
                        LastUpdated = lastUpdated,
                        IsPartiallySupported = isPartiallySupported,
                    };
                },
                _logger,
                cancellationToken);

            fileStatuses.RemoveAll((fs) => existingParams.Any((param) => param.Uri == fs.Uri && (param.Status != SearchParameterStatus.Initialized)));

            if (fileStatuses.Count == 0)
            {
                _logger.LogInformation("No Search Parameters need to be initialized.");
                return;
            }

            using var resourceExistCmd = new SqlCommand();
            resourceExistCmd.CommandType = CommandType.Text;
            resourceExistCmd.CommandText = "SELECT TOP 1 1 FROM dbo.Resource";
            var hasResources = await resourceExistCmd.ExecuteScalarAsync<int?>(_sqlRetryService, _logger, cancellationToken) == 1;

            fileStatuses.ForEach(fs =>
            {
                fs.Status = hasResources ? SearchParameterStatus.Supported : SearchParameterStatus.Enabled;
                fs.LastUpdated = existingParams.FirstOrDefault(p => p.Uri == fs.Uri)?.LastUpdated ?? fs.LastUpdated;
            });

            using var cmd = new SqlCommand();
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = "dbo.MergeSearchParams";

            new SearchParamListTableValuedParameterDefinition("@SearchParams").AddParameter(cmd.Parameters, new SearchParamListRowGenerator().GenerateRows(fileStatuses));

            try
            {
                await cmd.ExecuteNonQueryAsync(_sqlRetryService, _logger, cancellationToken);
                _logger.LogInformation("Number of Search Parameters initialized: {Number}", fileStatuses.Count);
            }
            catch (SqlException ex) when (ex.Number == 50001)
            {
                if (retryDepth >= 3)
                {
                    _logger.LogError("Maximum retry attempts reached for initializing search parameter statuses.");
                    throw;
                }

                _logger.LogInformation("Concurrent update detected, retrying");
                await InitializeSearchParameterStatuses(cancellationToken, retryDepth + 1);
            }
        }

        private int GetStringId(FhirMemoryCache<int> cache, string stringValue, StoredProcedure sproc)
        {
            if (cache.TryGet(stringValue, out int id))
            {
                return id;
            }

            _logger.LogInformation("Cache miss for string ID on {Sproc}", sproc);

            using var cmd = new SqlCommand();
            cmd.CommandType = CommandType.StoredProcedure;
#pragma warning disable CA2100 // Review SQL queries for security vulnerabilities. The stored procedure name is not influenced by user input, but determined by the code, so this is not vulnerable to SQL injection.
            cmd.CommandText = sproc.ProcedureName;
#pragma warning restore CA2100 // Review SQL queries for security vulnerabilities
            cmd.Parameters.AddWithValue("@stringValue", stringValue);

            // Forgive me father, I have sinned.
            // In ideal world I should make this method async, but that spirals out of control and forces changes in all RowGenerators (about 35 files)
            // and overall logic of preparing data for insert.
            id = cmd.ExecuteScalarAsync<int>(_sqlRetryService, _logger, CancellationToken.None).GetAwaiter().GetResult();
            cache.TryAdd(stringValue, id);
            return id;
        }

        private void ThrowIfNotInitialized()
        {
            ThrowIfCurrentSchemaVersionIsNull();

            if (_highestInitializedVersion < _schemaInformation.MinimumSupportedVersion)
            {
                _logger.LogError($"The {nameof(SqlServerFhirModel)} instance has not run the initialization required for minimum supported schema version");
                throw new ServiceUnavailableException();
            }

            if (_highestInitializedVersion < _schemaInformation.Current)
            {
                _logger.LogWarning($"The {nameof(SqlServerFhirModel)} instance has not run the initialization required for the current schema version");
            }
        }

        private void ThrowIfCurrentSchemaVersionIsNull()
        {
            if (_schemaInformation.Current == null)
            {
                _logger.LogError($"The SQL schema is yet to be initialized.");
                throw new ServiceUnavailableException();
            }

            // During schema initialization, once the base schema is initialized, CurrentVersion is set as 0 in InstanceSchema table and making progress to apply full schema snapshot file.
            if (_schemaInformation.Current == 0)
            {
                _logger.LogError($"The SQL Schema initialization is in progress.");
                throw new ServiceUnavailableException();
            }
        }

        private async Task LegacyInitializeBase(CancellationToken cancellationToken)
        {
            using (IScoped<SqlConnectionWrapperFactory> scopedSqlConnectionWrapperFactory = _scopedSqlConnectionWrapperFactory.Invoke())
            using (SqlConnectionWrapper sqlConnectionWrapper = await scopedSqlConnectionWrapperFactory.Value.ObtainSqlConnectionWrapperAsync(cancellationToken, true))
            using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateRetrySqlCommand())
            {
                // Synchronous calls are used because this code is executed on startup and doesn't need to be async.
                // Additionally, XUnit task scheduler constraints prevent async calls from being easily tested.
                sqlCommandWrapper.CommandText = @"
                        SET XACT_ABORT ON
                        BEGIN TRANSACTION

                        INSERT INTO dbo.ResourceType (Name)
                        SELECT value FROM string_split(@resourceTypes, ',')
                        EXCEPT SELECT Name FROM dbo.ResourceType WITH (TABLOCKX);

                        -- result set 1
                        SELECT ResourceTypeId, Name FROM dbo.ResourceType;

                        ;WITH Input AS (
                          SELECT DISTINCT
                                 j.Uri,
                                 CAST(j.IsPartiallySupported AS bit) AS IsPartiallySupported
                          FROM OPENJSON(@searchParams)
                          WITH (Uri varchar(128) '$.Uri', IsPartiallySupported bit '$.IsPartiallySupported') AS j
                        )
                        INSERT dbo.SearchParam (Uri, Status, LastUpdated, IsPartiallySupported)
                        SELECT i.Uri, 'Initialized', SYSDATETIMEOFFSET(), i.IsPartiallySupported
                        FROM Input AS i
                        WHERE NOT EXISTS (SELECT 1 FROM dbo.SearchParam AS sp WHERE sp.Uri = i.Uri);

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

                string searchParametersJson = JsonConvert.SerializeObject(_searchParameterDefinitionManager.AllSearchParameters.Select(p => new { Uri = p.Url, IsPartiallySupported = p.IsPartiallySupported }));
                string commaSeparatedResourceTypes = string.Join(",", ModelInfoProvider.GetResourceTypeNames());
                string commaSeparatedClaimTypes = string.Join(',', _securityConfiguration.PrincipalClaims);
                string commaSeparatedCompartmentTypes = string.Join(',', ModelInfoProvider.GetCompartmentTypeNames());

                sqlCommandWrapper.Parameters.AddWithValue("@searchParams", searchParametersJson);
                sqlCommandWrapper.Parameters.AddWithValue("@resourceTypes", commaSeparatedResourceTypes);
                sqlCommandWrapper.Parameters.AddWithValue("@claimTypes", commaSeparatedClaimTypes);
                sqlCommandWrapper.Parameters.AddWithValue("@compartmentTypes", commaSeparatedCompartmentTypes);

                using (SqlDataReader reader = await sqlCommandWrapper.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken))
                {
                    var resourceTypeToId = new Dictionary<string, short>(StringComparer.Ordinal);
                    var resourceTypeIdToTypeName = new Dictionary<short, string>();
                    var searchParamUriToId = new Dictionary<Uri, short>();
                    var claimNameToId = new Dictionary<string, byte>(StringComparer.Ordinal);
                    var compartmentTypeToId = new Dictionary<string, byte>();

                    // result set 1
                    short lowestResourceTypeId = short.MaxValue;
                    short highestResourceTypeId = short.MinValue;
                    while (await reader.ReadAsync(cancellationToken))
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
                    await reader.NextResultAsync(cancellationToken);

                    while (await reader.ReadAsync(cancellationToken))
                    {
                        (string uri, short searchParamId) = reader.ReadRow(VLatest.SearchParam.Uri, VLatest.SearchParam.SearchParamId);
                        searchParamUriToId.Add(new Uri(uri), searchParamId);
                    }

                    // result set 3
                    await reader.NextResultAsync(cancellationToken);

                    while (await reader.ReadAsync(cancellationToken))
                    {
                        (byte id, string claimTypeName) = reader.ReadRow(VLatest.ClaimType.ClaimTypeId, VLatest.ClaimType.Name);
                        claimNameToId.Add(claimTypeName, id);
                    }

                    // result set 4
                    await reader.NextResultAsync(cancellationToken);

                    while (await reader.ReadAsync(cancellationToken))
                    {
                        (byte id, string compartmentName) = reader.ReadRow(VLatest.CompartmentType.CompartmentTypeId, VLatest.CompartmentType.Name);
                        compartmentTypeToId.Add(compartmentName, id);
                    }

                    // result set 5
                    await reader.NextResultAsync(cancellationToken);

                    _systemToId = new FhirMemoryCache<int>("systemToId", _logger, ignoreCase: true);
                    bool systemWarningLogged = false;
                    while (await reader.ReadAsync(cancellationToken))
                    {
                        var (value, systemId) = reader.ReadRow(VLatest.System.Value, VLatest.System.SystemId);

                        if (!_systemToId.TryAdd(value, systemId) && !systemWarningLogged)
                        {
                            _logger.LogWarning($"Cache '{_systemToId.Name}' reached the limit of {_systemToId.CacheMemoryLimit} bytes (with {_systemToId.Count} cached elements).");
                            systemWarningLogged = true;
                        }
                    }

                    // result set 6
                    await reader.NextResultAsync(cancellationToken);

                    _quantityCodeToId = new FhirMemoryCache<int>("quantityCodeToId", _logger, ignoreCase: true);
                    bool quantityCodeWarningLogged = false;
                    while (await reader.ReadAsync(cancellationToken))
                    {
                        (string value, int quantityCodeId) = reader.ReadRow(VLatest.QuantityCode.Value, VLatest.QuantityCode.QuantityCodeId);

                        if (!_quantityCodeToId.TryAdd(value, quantityCodeId) && !quantityCodeWarningLogged)
                        {
                            _logger.LogWarning($"Cache '{_quantityCodeToId.Name}' reached the limit of {_quantityCodeToId.CacheMemoryLimit} bytes (with {_quantityCodeToId.Count} cached elements).");
                            quantityCodeWarningLogged = true;
                        }
                    }

                    _resourceTypeToId = resourceTypeToId;
                    _resourceTypeIdToTypeName = resourceTypeIdToTypeName;
                    _searchParamUriToId = searchParamUriToId;
                    _claimNameToId = claimNameToId;
                    _compartmentTypeToId = compartmentTypeToId;
                    _resourceTypeIdRange = (lowestResourceTypeId, highestResourceTypeId);
                }
            }
        }

        private async Task LegacyInitializeSearchParameterStatuses(CancellationToken cancellationToken)
        {
            using (IScoped<SqlConnectionWrapperFactory> scopedSqlConnectionWrapperFactory = _scopedSqlConnectionWrapperFactory.Invoke())
            using (SqlConnectionWrapper sqlConnectionWrapper = await scopedSqlConnectionWrapperFactory.Value.ObtainSqlConnectionWrapperAsync(cancellationToken, true))
            using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateRetrySqlCommand())
            {
                _logger.LogInformation("Initializing search parameters statuses.");
                sqlCommandWrapper.CommandText = @"
                        SET XACT_ABORT ON;
                        BEGIN TRANSACTION;
                        DECLARE @lastUpdated datetimeoffset(7) = SYSDATETIMEOFFSET();

                        UPDATE dbo.SearchParam
                        SET Status = sps.Status, LastUpdated = @lastUpdated, IsPartiallySupported = sps.IsPartiallySupported
                        FROM dbo.SearchParam INNER JOIN @searchParamStatuses as sps
                        ON dbo.SearchParam.Uri = sps.Uri
                        WHERE dbo.SearchParam.Status = 'Initialized' OR dbo.SearchParam.IsPartiallySupported IS NULL OR dbo.SearchParam.LastUpdated IS NULL;

                        SELECT @RowsAffected = @@ROWCOUNT;
                        COMMIT TRANSACTION;";

                IEnumerable<ResourceSearchParameterStatus> statuses = _filebasedSearchParameterStatusDataStore
                    .GetSearchParameterStatuses(cancellationToken).GetAwaiter().GetResult();

                // Use the appropriate collection based on schema version
                bool includeLastUpdated = _schemaInformation.Current >= SchemaVersionConstants.SearchParameterOptimisticConcurrency;
                var collection = new SearchParameterStatusCollection(includeLastUpdated);
                collection.AddRange(statuses);

                // Use the appropriate table type based on schema version
                string tableTypeName = _schemaInformation.Current >= SchemaVersionConstants.SearchParameterOptimisticConcurrency
                    ? "dbo.SearchParamList"
                    : "dbo.SearchParamTableType_2";

                var tableValuedParameter = new SqlParameter
                {
                    ParameterName = "searchParamStatuses",
                    SqlDbType = SqlDbType.Structured,
                    Value = collection,
                    Direction = ParameterDirection.Input,
                    TypeName = tableTypeName,
                };

                sqlCommandWrapper.Parameters.Add(tableValuedParameter);
                sqlCommandWrapper.Parameters.Add(new SqlParameter("@RowsAffected", SqlDbType.Int) { Direction = ParameterDirection.Output });

                await sqlCommandWrapper.ExecuteNonQueryAsync(cancellationToken);

                int rowsAffected = (int)sqlCommandWrapper.Parameters["@RowsAffected"].Value;
                _logger.LogInformation("Number of Search Parameters initialized: {RowsAffected}.", rowsAffected);
            }
        }
    }
}
