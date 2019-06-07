// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Configs;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.Fhir.ValueSets;
using Microsoft.IO;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    /// <summary>
    /// A SQL Server-backed <see cref="IFhirDataStore"/>.
    /// </summary>
    internal class SqlServerFhirDataStore : IFhirDataStore, IProvideCapability
    {
        internal static readonly Encoding ResourceEncoding = new UnicodeEncoding(bigEndian: false, byteOrderMark: false);

        private readonly SqlServerDataStoreConfiguration _configuration;
        private readonly SqlServerFhirModel _model;
        private readonly SearchParameterToSearchValueTypeMap _searchParameterTypeMap;
        private readonly V1.UpsertResourceTvpGenerator<ResourceMetadata> _upsertResourceTvpGenerator;
        private readonly RecyclableMemoryStreamManager _memoryStreamManager;
        private readonly ILogger<SqlServerFhirDataStore> _logger;

        public SqlServerFhirDataStore(
            SqlServerDataStoreConfiguration configuration,
            SqlServerFhirModel model,
            SearchParameterToSearchValueTypeMap searchParameterTypeMap,
            V1.UpsertResourceTvpGenerator<ResourceMetadata> upsertResourceTvpGenerator,
            ILogger<SqlServerFhirDataStore> logger)
        {
            EnsureArg.IsNotNull(configuration, nameof(configuration));
            EnsureArg.IsNotNull(model, nameof(model));
            EnsureArg.IsNotNull(searchParameterTypeMap, nameof(searchParameterTypeMap));
            EnsureArg.IsNotNull(upsertResourceTvpGenerator, nameof(upsertResourceTvpGenerator));
            EnsureArg.IsNotNull(logger, nameof(logger));
            _configuration = configuration;
            _model = model;
            _searchParameterTypeMap = searchParameterTypeMap;
            _upsertResourceTvpGenerator = upsertResourceTvpGenerator;
            _logger = logger;
            _memoryStreamManager = new RecyclableMemoryStreamManager();
        }

        public async Task<UpsertOutcome> UpsertAsync(ResourceWrapper resource, WeakETag weakETag, bool allowCreate, bool keepHistory, CancellationToken cancellationToken)
        {
            await _model.EnsureInitialized();

            int etag = 0;
            if (weakETag != null && !int.TryParse(weakETag.VersionId, out etag))
            {
                throw new ResourceConflictException(weakETag);
            }

            var resourceMetadata = new ResourceMetadata(
                resource.CompartmentIndices,
                resource.SearchIndices?.ToLookup(e => _searchParameterTypeMap.GetSearchValueType(e)),
                resource.LastModifiedClaims);

            using (var connection = new SqlConnection(_configuration.ConnectionString))
            {
                await connection.OpenAsync(cancellationToken);

                using (var command = connection.CreateCommand())
                using (var stream = new RecyclableMemoryStream(_memoryStreamManager))
                using (var gzipStream = new GZipStream(stream, CompressionMode.Compress))
                using (var writer = new StreamWriter(gzipStream, ResourceEncoding))
                {
                    writer.Write(resource.RawResource.Data);
                    writer.Flush();

                    stream.Seek(0, 0);

                    V1.UpsertResource.PopulateCommand(
                        command,
                        baseResourceSurrogateId: ResourceSurrogateIdHelper.LastUpdatedToResourceSurrogateId(resource.LastModified.UtcDateTime),
                        resourceTypeId: _model.GetResourceTypeId(resource.ResourceTypeName),
                        resourceId: resource.ResourceId,
                        eTag: weakETag == null ? null : (int?)etag,
                        allowCreate: allowCreate,
                        isDeleted: resource.IsDeleted,
                        keepHistory: keepHistory,
                        requestMethod: resource.Request.Method,
                        rawResource: stream,
                        tableValuedParameters: _upsertResourceTvpGenerator.Generate(resourceMetadata));

                    try
                    {
                        var newVersion = (int?)await command.ExecuteScalarAsync(cancellationToken);
                        if (newVersion == null)
                        {
                            // indicates a redundant delete
                            return null;
                        }

                        resource.Version = newVersion.ToString();

                        return new UpsertOutcome(resource, newVersion == 1 ? SaveOutcomeType.Created : SaveOutcomeType.Updated);
                    }
                    catch (SqlException e)
                    {
                        switch (e.Number)
                        {
                            case SqlErrorCodes.NotFound:
                                throw new MethodNotAllowedException(Core.Resources.ResourceCreationNotAllowed);
                            case SqlErrorCodes.PreconditionFailed:
                                throw new ResourceConflictException(weakETag);
                            default:
                                _logger.LogError(e, "Error from SQL database on upsert");
                                throw;
                        }
                    }
                }
            }
        }

        public async Task<ResourceWrapper> GetAsync(ResourceKey key, CancellationToken cancellationToken)
        {
            await _model.EnsureInitialized();

            using (var connection = new SqlConnection(_configuration.ConnectionString))
            {
                await connection.OpenAsync(cancellationToken);

                int? requestedVersion = null;
                if (!string.IsNullOrEmpty(key.VersionId))
                {
                    if (!int.TryParse(key.VersionId, out var parsedVersion))
                    {
                        return null;
                    }

                    requestedVersion = parsedVersion;
                }

                using (SqlCommand command = connection.CreateCommand())
                {
                    V1.ReadResource.PopulateCommand(
                        command,
                        resourceTypeId: _model.GetResourceTypeId(key.ResourceType),
                        resourceId: key.Id,
                        version: requestedVersion);

                    using (SqlDataReader sqlDataReader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken))
                    {
                        if (!sqlDataReader.Read())
                        {
                            return null;
                        }

                        var resourceTable = V1.Resource;

                        (long resourceSurrogateId, int version, bool isDeleted, bool isHistory, Stream rawResourceStream) = sqlDataReader.ReadRow(
                            resourceTable.ResourceSurrogateId,
                            resourceTable.Version,
                            resourceTable.IsDeleted,
                            resourceTable.IsHistory,
                            resourceTable.RawResource);

                        string rawResource;

                        using (rawResourceStream)
                        using (var gzipStream = new GZipStream(rawResourceStream, CompressionMode.Decompress))
                        using (var reader = new StreamReader(gzipStream, ResourceEncoding))
                        {
                            rawResource = await reader.ReadToEndAsync();
                        }

                        return new ResourceWrapper(
                            key.Id,
                            version.ToString(CultureInfo.InvariantCulture),
                            key.ResourceType,
                            new RawResource(rawResource, FhirResourceFormat.Json),
                            null,
                            new DateTimeOffset(ResourceSurrogateIdHelper.ResourceSurrogateIdToLastUpdated(resourceSurrogateId), TimeSpan.Zero),
                            isDeleted,
                            searchIndices: null,
                            compartmentIndices: null,
                            lastModifiedClaims: null)
                        {
                            IsHistory = isHistory,
                        };
                    }
                }
            }
        }

        public async Task HardDeleteAsync(ResourceKey key, CancellationToken cancellationToken)
        {
            await _model.EnsureInitialized();

            using (var connection = new SqlConnection(_configuration.ConnectionString))
            {
                await connection.OpenAsync(cancellationToken);

                using (var command = connection.CreateCommand())
                {
                    V1.HardDeleteResource.PopulateCommand(command, resourceTypeId: _model.GetResourceTypeId(key.ResourceType), resourceId: key.Id);

                    await command.ExecuteNonQueryAsync(cancellationToken);
                }
            }
        }

        public void Build(IListedCapabilityStatement statement)
        {
            EnsureArg.IsNotNull(statement, nameof(statement));

            foreach (var resource in ModelInfoProvider.GetResourceTypeNames())
            {
                statement.BuildRestResourceComponent(resource, builder =>
                {
                    builder.AddResourceVersionPolicy(ResourceVersionPolicy.NoVersion);
                    builder.AddResourceVersionPolicy(ResourceVersionPolicy.Versioned);
                    builder.AddResourceVersionPolicy(ResourceVersionPolicy.VersionedUpdate);
                    builder.ReadHistory = true;
                    builder.UpdateCreate = true;
                });
            }
        }
    }
}
