﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.SqlServer.Configs;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.IO;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    /// <summary>
    /// A SQL Server-backed <see cref="IFhirDataStore"/>.
    /// </summary>
    public class SqlServerFhirDataStore : IFhirDataStore, IProvideCapability
    {
        private static readonly Encoding ResourceEncoding = new UnicodeEncoding(bigEndian: false, byteOrderMark: false);

        private readonly SqlServerDataStoreConfiguration _configuration;
        private readonly SqlServerFhirModel _model;
        private readonly RecyclableMemoryStreamManager _memoryStreamManager;
        private readonly ILogger<SqlServerFhirDataStore> _logger;

        public SqlServerFhirDataStore(SqlServerDataStoreConfiguration configuration, SqlServerFhirModel model, ILogger<SqlServerFhirDataStore> logger)
        {
            EnsureArg.IsNotNull(configuration, nameof(configuration));
            EnsureArg.IsNotNull(model, nameof(model));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _configuration = configuration;
            _model = model;
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

            using (var connection = new SqlConnection(_configuration.ConnectionString))
            {
                await connection.OpenAsync(cancellationToken);

                using (var command = connection.CreateCommand())
                using (var ms = new RecyclableMemoryStream(_memoryStreamManager))
                using (var gzipStream = new GZipStream(ms, CompressionMode.Compress))
                using (var writer = new StreamWriter(gzipStream, ResourceEncoding))
                {
                    writer.Write(resource.RawResource.Data);
                    writer.Flush();

                    ms.Seek(0, 0);

                    V1.UpsertResource.PopulateCommand(
                        command,
                        resourceTypeId: _model.GetResourceTypeId(resource.ResourceTypeName),
                        resourceId: resource.ResourceId,
                        eTag: weakETag == null ? null : (int?)etag,
                        allowCreate: allowCreate,
                        isDeleted: resource.IsDeleted,
                        updatedDateTime: resource.LastModified,
                        keepHistory: keepHistory,
                        requestMethod: resource.Request.Method,
                        rawResource: ms);

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
                                throw new MethodNotAllowedException(Resources.ResourceCreationNotAllowed);
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

                using (SqlCommand command = connection.CreateCommand())
                {
                    var resourceTable = V1.Resource;
                    command.Parameters.AddFromColumnWithDefaultName(resourceTable.ResourceTypeId, _model.GetResourceTypeId(key.ResourceType));
                    command.Parameters.AddFromColumnWithDefaultName(resourceTable.ResourceId, key.Id);

                    bool versionedRead = !string.IsNullOrEmpty(key.VersionId);

                    if (versionedRead)
                    {
                        command.CommandText = @"
                            SELECT Version, LastUpdated, IsDeleted, IsHistory, RawResource 
                            FROM dbo.Resource
                            WHERE ResourceTypeId = @resourceTypeId AND ResourceId = @resourceId AND Version = @version";

                        if (!int.TryParse(key.VersionId, out var version))
                        {
                            return null;
                        }

                        command.Parameters.AddFromColumnWithDefaultName(resourceTable.Version, version);
                    }
                    else
                    {
                        command.CommandText = @"
                            SELECT Version, LastUpdated, IsDeleted, IsHistory, RawResource 
                            FROM dbo.Resource
                            WHERE ResourceTypeId = @resourceTypeId AND ResourceId = @resourceId AND IsHistory = 0";
                    }

                    using (SqlDataReader sqlDataReader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken))
                    {
                        if (!sqlDataReader.Read())
                        {
                            return null;
                        }

                        (int version, DateTime lastModified, bool isDeleted, bool isHistory, Stream rawResourceStream) = sqlDataReader.ReadRow(
                            resourceTable.Version,
                            resourceTable.LastUpdated,
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
                            new RawResource(rawResource, ResourceFormat.Json),
                            null,
                            new DateTimeOffset(lastModified, TimeSpan.Zero),
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

        Task<ExportJobOutcome> IFhirDataStore.CreateExportJobAsync(ExportJobRecord jobRecord, CancellationToken cancellationToken) => throw new System.NotImplementedException();

        Task<ExportJobOutcome> IFhirDataStore.GetExportJobAsync(string jobId, CancellationToken cancellationToken) => throw new System.NotImplementedException();

        Task<ExportJobOutcome> IFhirDataStore.ReplaceExportJobAsync(ExportJobRecord jobRecord, WeakETag eTag, CancellationToken cancellationToken) => throw new System.NotImplementedException();

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
