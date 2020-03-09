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
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
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
        private readonly VLatest.UpsertResourceTvpGenerator<ResourceMetadata> _upsertResourceTvpGenerator;
        private readonly RecyclableMemoryStreamManager _memoryStreamManager;
        private readonly CoreFeatureConfiguration _coreFeatures;
        private readonly SqlConnectionWrapperFactory _sqlConnectionWrapperFactory;
        private readonly ILogger<SqlServerFhirDataStore> _logger;

        public SqlServerFhirDataStore(
            SqlServerDataStoreConfiguration configuration,
            SqlServerFhirModel model,
            SearchParameterToSearchValueTypeMap searchParameterTypeMap,
            VLatest.UpsertResourceTvpGenerator<ResourceMetadata> upsertResourceTvpGenerator,
            IOptions<CoreFeatureConfiguration> coreFeatures,
            SqlConnectionWrapperFactory sqlConnectionWrapperFactory,
            ILogger<SqlServerFhirDataStore> logger)
        {
            EnsureArg.IsNotNull(configuration, nameof(configuration));
            EnsureArg.IsNotNull(model, nameof(model));
            EnsureArg.IsNotNull(searchParameterTypeMap, nameof(searchParameterTypeMap));
            EnsureArg.IsNotNull(upsertResourceTvpGenerator, nameof(upsertResourceTvpGenerator));
            EnsureArg.IsNotNull(coreFeatures, nameof(coreFeatures));
            EnsureArg.IsNotNull(sqlConnectionWrapperFactory, nameof(sqlConnectionWrapperFactory));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _configuration = configuration;
            _model = model;
            _searchParameterTypeMap = searchParameterTypeMap;
            _upsertResourceTvpGenerator = upsertResourceTvpGenerator;
            _coreFeatures = coreFeatures.Value;
            _sqlConnectionWrapperFactory = sqlConnectionWrapperFactory;
            _logger = logger;

            _memoryStreamManager = new RecyclableMemoryStreamManager();
        }

        public async Task<UpsertOutcome> UpsertAsync(ResourceWrapper resource, WeakETag weakETag, bool allowCreate, bool keepHistory, CancellationToken cancellationToken)
        {
            await _model.EnsureInitialized();

            int etag = 0;
            if (weakETag != null && !int.TryParse(weakETag.VersionId, out etag))
            {
                // Set the etag to a sentinel value to enable expected failure paths when updating with both existing and nonexistent resources.
                etag = -1;
            }

            var resourceMetadata = new ResourceMetadata(
                resource.CompartmentIndices,
                resource.SearchIndices?.ToLookup(e => _searchParameterTypeMap.GetSearchValueType(e)),
                resource.LastModifiedClaims);

            using (SqlConnectionWrapper sqlConnectionWrapper = _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapper(true))
            {
                using (SqlCommand command = sqlConnectionWrapper.CreateSqlCommand())
                using (var stream = new RecyclableMemoryStream(_memoryStreamManager))
                using (var gzipStream = new GZipStream(stream, CompressionMode.Compress))
                using (var writer = new StreamWriter(gzipStream, ResourceEncoding))
                {
                    writer.Write(resource.RawResource.Data);
                    writer.Flush();

                    stream.Seek(0, 0);

                    VLatest.UpsertResource.PopulateCommand(
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
                            case SqlErrorCodes.PreconditionFailed:
                                throw new PreconditionFailedException(string.Format(Core.Resources.ResourceVersionConflict, weakETag?.VersionId));
                            case SqlErrorCodes.NotFound:
                                if (weakETag != null)
                                {
                                    throw new ResourceNotFoundException(string.Format(Core.Resources.ResourceNotFoundByIdAndVersion, resource.ResourceTypeName, resource.ResourceId, weakETag.VersionId));
                                }

                                goto default;
                            case SqlErrorCodes.MethodNotAllowed:
                                throw new MethodNotAllowedException(Core.Resources.ResourceCreationNotAllowed);
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

            using (SqlConnectionWrapper sqlConnectionWrapper = _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapper(true))
            {
                int? requestedVersion = null;
                if (!string.IsNullOrEmpty(key.VersionId))
                {
                    if (!int.TryParse(key.VersionId, out var parsedVersion))
                    {
                        return null;
                    }

                    requestedVersion = parsedVersion;
                }

                using (SqlCommand command = sqlConnectionWrapper.CreateSqlCommand())
                {
                    VLatest.ReadResource.PopulateCommand(
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

                        var resourceTable = VLatest.Resource;

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

            using (SqlConnectionWrapper sqlConnectionWrapper = _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapper(true))
            {
                using (var command = sqlConnectionWrapper.CreateSqlCommand())
                {
                    VLatest.HardDeleteResource.PopulateCommand(command, resourceTypeId: _model.GetResourceTypeId(key.ResourceType), resourceId: key.Id);

                    await command.ExecuteNonQueryAsync(cancellationToken);
                }
            }
        }

        public void Build(ICapabilityStatementBuilder builder)
        {
            EnsureArg.IsNotNull(builder, nameof(builder));

            builder.AddDefaultResourceInteractions()
                   .AddDefaultSearchParameters()
                   .AddDefaultRestSearchParams();

            if (_coreFeatures.SupportsBatch)
            {
                // Batch supported added in listedCapability
                builder.AddRestInteraction(SystemRestfulInteraction.Batch);
            }

            if (_coreFeatures.SupportsTransaction)
            {
                // Transaction supported added in listedCapability
                builder.AddRestInteraction(SystemRestfulInteraction.Transaction);
            }
        }
    }
}
