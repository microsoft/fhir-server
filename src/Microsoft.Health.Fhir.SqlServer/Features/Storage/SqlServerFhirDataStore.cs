// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
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
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.Fhir.ValueSets;
using Microsoft.Health.SqlServer.Configs;
using Microsoft.Health.SqlServer.Features.Client;
using Microsoft.Health.SqlServer.Features.Schema;
using Microsoft.Health.SqlServer.Features.Storage;
using Microsoft.IO;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    /// <summary>
    /// A SQL Server-backed <see cref="IFhirDataStore"/>.
    /// </summary>
    internal class SqlServerFhirDataStore : IFhirDataStore, IProvideCapability
    {
        private readonly SqlServerDataStoreConfiguration _configuration;
        private readonly SqlServerFhirModel _model;
        private readonly SearchParameterToSearchValueTypeMap _searchParameterTypeMap;
        private readonly VLatest.UpsertResourceTvpGenerator<ResourceMetadata> _upsertResourceTvpGeneratorVLatest;
        private readonly RecyclableMemoryStreamManager _memoryStreamManager;
        private readonly CoreFeatureConfiguration _coreFeatures;
        private readonly SqlConnectionWrapperFactory _sqlConnectionWrapperFactory;
        private readonly ILogger<SqlServerFhirDataStore> _logger;
        private readonly SchemaInformation _schemaInformation;

        public SqlServerFhirDataStore(
            SqlServerDataStoreConfiguration configuration,
            SqlServerFhirModel model,
            SearchParameterToSearchValueTypeMap searchParameterTypeMap,
            VLatest.UpsertResourceTvpGenerator<ResourceMetadata> upsertResourceTvpGeneratorVLatest,
            IOptions<CoreFeatureConfiguration> coreFeatures,
            SqlConnectionWrapperFactory sqlConnectionWrapperFactory,
            ILogger<SqlServerFhirDataStore> logger,
            SchemaInformation schemaInformation)
        {
            EnsureArg.IsNotNull(configuration, nameof(configuration));
            EnsureArg.IsNotNull(model, nameof(model));
            EnsureArg.IsNotNull(searchParameterTypeMap, nameof(searchParameterTypeMap));
            EnsureArg.IsNotNull(upsertResourceTvpGeneratorVLatest, nameof(upsertResourceTvpGeneratorVLatest));
            EnsureArg.IsNotNull(coreFeatures, nameof(coreFeatures));
            EnsureArg.IsNotNull(sqlConnectionWrapperFactory, nameof(sqlConnectionWrapperFactory));
            EnsureArg.IsNotNull(logger, nameof(logger));
            EnsureArg.IsNotNull(schemaInformation, nameof(schemaInformation));

            _configuration = configuration;
            _model = model;
            _searchParameterTypeMap = searchParameterTypeMap;
            _upsertResourceTvpGeneratorVLatest = upsertResourceTvpGeneratorVLatest;
            _coreFeatures = coreFeatures.Value;
            _sqlConnectionWrapperFactory = sqlConnectionWrapperFactory;
            _logger = logger;
            _schemaInformation = schemaInformation;

            _memoryStreamManager = new RecyclableMemoryStreamManager();
        }

        public async Task<UpsertOutcome> UpsertAsync(ResourceWrapper resource, WeakETag weakETag, bool allowCreate, bool keepHistory, CancellationToken cancellationToken)
        {
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
            using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateSqlCommand())
            using (var stream = new RecyclableMemoryStream(_memoryStreamManager))
            {
                CompressedRawResourceConverter.WriteCompressedRawResource(stream, resource.RawResource.Data);

                stream.Seek(0, 0);

                VLatest.UpsertResource.PopulateCommand(
                sqlCommandWrapper,
                baseResourceSurrogateId: ResourceSurrogateIdHelper.LastUpdatedToResourceSurrogateId(resource.LastModified.UtcDateTime),
                resourceTypeId: _model.GetResourceTypeId(resource.ResourceTypeName),
                resourceId: resource.ResourceId,
                eTag: weakETag == null ? null : (int?)etag,
                allowCreate: allowCreate,
                isDeleted: resource.IsDeleted,
                keepHistory: keepHistory,
                requestMethod: resource.Request.Method,
                rawResource: stream,
                tableValuedParameters: _upsertResourceTvpGeneratorVLatest.Generate(resourceMetadata));

                try
                {
                    var newVersion = (int?)await sqlCommandWrapper.ExecuteScalarAsync(cancellationToken);
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

        public async Task<ResourceWrapper> GetAsync(ResourceKey key, CancellationToken cancellationToken)
        {
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

                using (SqlCommandWrapper commandWrapper = sqlConnectionWrapper.CreateSqlCommand())
                {
                    VLatest.ReadResource.PopulateCommand(
                        commandWrapper,
                        resourceTypeId: _model.GetResourceTypeId(key.ResourceType),
                        resourceId: key.Id,
                        version: requestedVersion);

                    using (SqlDataReader sqlDataReader = await commandWrapper.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken))
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
                        {
                            rawResource = await CompressedRawResourceConverter.ReadCompressedRawResource(rawResourceStream);
                        }

                        bool isRawResourceMetaSet = false;

                        if (_schemaInformation.Current >= 4)
                        {
                            isRawResourceMetaSet = sqlDataReader.Read(resourceTable.IsRawResourceMetaSet, 5);
                        }

                        return new ResourceWrapper(
                            key.Id,
                            version.ToString(CultureInfo.InvariantCulture),
                            key.ResourceType,
                            new RawResource(rawResource, FhirResourceFormat.Json, isMetaSet: isRawResourceMetaSet),
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
            using (SqlConnectionWrapper sqlConnectionWrapper = _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapper(true))
            using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateSqlCommand())
            {
                VLatest.HardDeleteResource.PopulateCommand(sqlCommandWrapper, resourceTypeId: _model.GetResourceTypeId(key.ResourceType), resourceId: key.Id);

                await sqlCommandWrapper.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        public async Task UpdateSearchParameterHashBatchAsync(IReadOnlyCollection<ResourceWrapper> resources, CancellationToken cancellationToken)
        {
            // TODO: use bach command to update only hash values for list updateHashValueOnly
            // this is a place holder update until we batch update resources
            foreach (var resource in resources)
            {
                await UpsertAsync(resource, WeakETag.FromVersionId(resource.Version), false, true, cancellationToken);
            }
        }

        public async Task UpdateSearchParameterIndicesBatchAsync(IReadOnlyCollection<ResourceWrapper> resources, CancellationToken cancellationToken)
        {
            // TODO: use batch command to update both hash values and search index values for list updateSearchIndices

            // this is a place holder update until we batch update resources
            foreach (var resource in resources)
            {
                await UpsertAsync(resource, WeakETag.FromVersionId(resource.Version), false, true, cancellationToken);
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

        public Task<ResourceWrapper> UpdateSearchIndexForResourceAsync(ResourceWrapper resourceWrapper, WeakETag weakETag, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }
    }
}
