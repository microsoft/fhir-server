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
            using (var connection = new SqlConnection(_configuration.ConnectionString))
            {
                await connection.OpenAsync(cancellationToken);

                using (var command = connection.CreateCommand())
                {
                    command.CommandType = CommandType.StoredProcedure;
                    command.CommandText = "dbo.UpsertResource";

                    command.Parameters.AddWithValue("@resourceTypeId", _model.GetResourceTypeId(resource.ResourceTypeName));
                    command.Parameters.Add(new SqlParameter("@resourceId", SqlDbType.VarChar, 64) { Value = resource.ResourceId });

                    int etag = 0;
                    if (weakETag != null && !int.TryParse(weakETag.VersionId, out etag))
                    {
                        throw new ResourceConflictException(weakETag);
                    }

                    command.Parameters.Add("@eTag", SqlDbType.Int).Value = weakETag == null ? null : (int?)etag;
                    command.Parameters.AddWithValue("@allowCreate", allowCreate);
                    command.Parameters.AddWithValue("@isDeleted", resource.IsDeleted);
                    command.Parameters.AddWithValue("@updatedDateTime", resource.LastModified);
                    command.Parameters.AddWithValue("@keepHistory", keepHistory);
                    command.Parameters.AddWithValue("@requestMethod", resource.Request.Method);

                    using (var ms = new RecyclableMemoryStream(_memoryStreamManager))
                    using (var gzipStream = new GZipStream(ms, CompressionMode.Compress))
                    {
                        using (var writer = new StreamWriter(gzipStream, ResourceEncoding))
                        {
                            writer.Write(resource.RawResource.Data);
                            writer.Flush();

                            ms.Seek(0, 0);

                            command.Parameters.Add("@rawResource", SqlDbType.VarBinary, (int)ms.Length).Value = ms;

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
            }
        }

        public async Task<ResourceWrapper> GetAsync(ResourceKey key, CancellationToken cancellationToken)
        {
            using (var connection = new SqlConnection(_configuration.ConnectionString))
            {
                await connection.OpenAsync(cancellationToken);

                using (SqlCommand command = connection.CreateCommand())
                {
                    command.Parameters.AddWithValue("@resourceTypeId", _model.GetResourceTypeId(key.ResourceType));
                    command.Parameters.AddWithValue("@resourceId", key.Id);

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

                        command.Parameters.AddWithValue("@version", version);
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

                        int version = sqlDataReader.GetInt32(0);
                        DateTime lastModified = sqlDataReader.GetDateTime(1);
                        var isDeleted = sqlDataReader.GetBoolean(2);
                        var isHistory = sqlDataReader.GetBoolean(3);

                        string rawResource;

                        using (var dataStream = sqlDataReader.GetStream(4))
                        using (var gzipStream = new GZipStream(dataStream, CompressionMode.Decompress))
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
            using (var connection = new SqlConnection(_configuration.ConnectionString))
            {
                await connection.OpenAsync(cancellationToken);

                using (var command = connection.CreateCommand())
                {
                    command.CommandType = CommandType.StoredProcedure;
                    command.CommandText = "dbo.HardDeleteResource";

                    command.Parameters.AddWithValue("@resourceTypeId", _model.GetResourceTypeId(key.ResourceType));
                    command.Parameters.Add(new SqlParameter("@resourceId", SqlDbType.VarChar, 64) { Value = key.Id });

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
