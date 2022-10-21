// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Abstractions.Exceptions;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.Fhir.SqlServer.Features.Search;
using Microsoft.Health.Fhir.ValueSets;
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
        private readonly RequestContextAccessor<IFhirRequestContext> _requestContextAccessor;
        private readonly ISqlServerFhirModel _model;
        private readonly SearchParameterToSearchValueTypeMap _searchParameterTypeMap;
        private readonly V6.UpsertResourceTvpGenerator<ResourceMetadata> _upsertResourceTvpGeneratorV6;
        private readonly V7.UpsertResourceTvpGenerator<ResourceMetadata> _upsertResourceTvpGeneratorV7;
        private readonly V13.UpsertResourceTvpGenerator<IReadOnlyList<ResourceWrapper>> _upsertResourceTvpGeneratorV13;
        private readonly V17.UpsertResourceTvpGenerator<IReadOnlyList<ResourceWrapper>> _upsertResourceTvpGeneratorV17;
        private readonly V18.UpsertResourceTvpGenerator<IReadOnlyList<ResourceWrapper>> _upsertResourceTvpGeneratorV18;
        private readonly V27.UpsertResourceTvpGenerator<IReadOnlyList<ResourceWrapper>> _upsertResourceTvpGeneratorV27;
        private readonly V40.UpsertResourceTvpGenerator<IReadOnlyList<ResourceWrapper>> _upsertResourceTvpGeneratorV40;
        private readonly V17.ReindexResourceTvpGenerator<IReadOnlyList<ResourceWrapper>> _reindexResourceTvpGeneratorV17;
        private readonly V17.BulkReindexResourcesTvpGenerator<IReadOnlyList<ResourceWrapper>> _bulkReindexResourcesTvpGeneratorV17;
        private readonly VLatest.UpsertResourceTvpGenerator<IReadOnlyList<ResourceWrapper>> _upsertResourceTvpGeneratorVLatest;
        private readonly VLatest.ReindexResourceTvpGenerator<IReadOnlyList<ResourceWrapper>> _reindexResourceTvpGeneratorVLatest;
        private readonly VLatest.BulkReindexResourcesTvpGenerator<IReadOnlyList<ResourceWrapper>> _bulkReindexResourcesTvpGeneratorVLatest;
        private readonly RecyclableMemoryStreamManager _memoryStreamManager;
        private readonly CoreFeatureConfiguration _coreFeatures;
        private readonly SqlConnectionWrapperFactory _sqlConnectionWrapperFactory;
        private readonly ICompressedRawResourceConverter _compressedRawResourceConverter;
        private readonly ILogger<SqlServerFhirDataStore> _logger;
        private readonly SchemaInformation _schemaInformation;
        private readonly IModelInfoProvider _modelInfoProvider;
        private readonly SqlQueueClient _sqlQueueClient;
        private static readonly object _watchDogLocker = new object();
        private static Operations.DefragWorker _defrag;

        public SqlServerFhirDataStore(
            ISqlServerFhirModel model,
            SearchParameterToSearchValueTypeMap searchParameterTypeMap,
            V6.UpsertResourceTvpGenerator<ResourceMetadata> upsertResourceTvpGeneratorV6,
            V7.UpsertResourceTvpGenerator<ResourceMetadata> upsertResourceTvpGeneratorV7,
            V13.UpsertResourceTvpGenerator<IReadOnlyList<ResourceWrapper>> upsertResourceTvpGeneratorV13,
            V17.UpsertResourceTvpGenerator<IReadOnlyList<ResourceWrapper>> upsertResourceTvpGeneratorV17,
            V18.UpsertResourceTvpGenerator<IReadOnlyList<ResourceWrapper>> upsertResourceTvpGeneratorV18,
            V27.UpsertResourceTvpGenerator<IReadOnlyList<ResourceWrapper>> upsertResourceTvpGeneratorV27,
            V40.UpsertResourceTvpGenerator<IReadOnlyList<ResourceWrapper>> upsertResourceTvpGeneratorV40,
            VLatest.UpsertResourceTvpGenerator<IReadOnlyList<ResourceWrapper>> upsertResourceTvpGeneratorVLatest,
            V17.ReindexResourceTvpGenerator<IReadOnlyList<ResourceWrapper>> reindexResourceTvpGeneratorV17,
            VLatest.ReindexResourceTvpGenerator<IReadOnlyList<ResourceWrapper>> reindexResourceTvpGeneratorVLatest,
            V17.BulkReindexResourcesTvpGenerator<IReadOnlyList<ResourceWrapper>> bulkReindexResourcesTvpGeneratorV17,
            VLatest.BulkReindexResourcesTvpGenerator<IReadOnlyList<ResourceWrapper>> bulkReindexResourcesTvpGeneratorVLatest,
            IOptions<CoreFeatureConfiguration> coreFeatures,
            SqlConnectionWrapperFactory sqlConnectionWrapperFactory,
            ICompressedRawResourceConverter compressedRawResourceConverter,
            ILogger<SqlServerFhirDataStore> logger,
            SchemaInformation schemaInformation,
            IModelInfoProvider modelInfoProvider,
            RequestContextAccessor<IFhirRequestContext> requestContextAccessor,
            SqlQueueClient sqlQueueClient)
        {
            _model = EnsureArg.IsNotNull(model, nameof(model));
            _searchParameterTypeMap = EnsureArg.IsNotNull(searchParameterTypeMap, nameof(searchParameterTypeMap));
            _upsertResourceTvpGeneratorV6 = EnsureArg.IsNotNull(upsertResourceTvpGeneratorV6, nameof(upsertResourceTvpGeneratorV6));
            _upsertResourceTvpGeneratorV7 = EnsureArg.IsNotNull(upsertResourceTvpGeneratorV7, nameof(upsertResourceTvpGeneratorV7));
            _upsertResourceTvpGeneratorV13 = EnsureArg.IsNotNull(upsertResourceTvpGeneratorV13, nameof(upsertResourceTvpGeneratorV13));
            _upsertResourceTvpGeneratorV17 = EnsureArg.IsNotNull(upsertResourceTvpGeneratorV17, nameof(upsertResourceTvpGeneratorV17));
            _upsertResourceTvpGeneratorV18 = EnsureArg.IsNotNull(upsertResourceTvpGeneratorV18, nameof(upsertResourceTvpGeneratorV18));
            _upsertResourceTvpGeneratorV27 = EnsureArg.IsNotNull(upsertResourceTvpGeneratorV27, nameof(upsertResourceTvpGeneratorV27));
            _upsertResourceTvpGeneratorV40 = EnsureArg.IsNotNull(upsertResourceTvpGeneratorV40, nameof(upsertResourceTvpGeneratorV40));
            _upsertResourceTvpGeneratorVLatest = EnsureArg.IsNotNull(upsertResourceTvpGeneratorVLatest, nameof(upsertResourceTvpGeneratorVLatest));
            _reindexResourceTvpGeneratorV17 = EnsureArg.IsNotNull(reindexResourceTvpGeneratorV17, nameof(reindexResourceTvpGeneratorV17));
            _reindexResourceTvpGeneratorVLatest = EnsureArg.IsNotNull(reindexResourceTvpGeneratorVLatest, nameof(reindexResourceTvpGeneratorVLatest));
            _bulkReindexResourcesTvpGeneratorV17 = EnsureArg.IsNotNull(bulkReindexResourcesTvpGeneratorV17, nameof(bulkReindexResourcesTvpGeneratorV17));
            _bulkReindexResourcesTvpGeneratorVLatest = EnsureArg.IsNotNull(bulkReindexResourcesTvpGeneratorVLatest, nameof(bulkReindexResourcesTvpGeneratorVLatest));
            _coreFeatures = EnsureArg.IsNotNull(coreFeatures?.Value, nameof(coreFeatures));
            _sqlConnectionWrapperFactory = EnsureArg.IsNotNull(sqlConnectionWrapperFactory, nameof(sqlConnectionWrapperFactory));
            _compressedRawResourceConverter = EnsureArg.IsNotNull(compressedRawResourceConverter, nameof(compressedRawResourceConverter));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
            _schemaInformation = EnsureArg.IsNotNull(schemaInformation, nameof(schemaInformation));
            _modelInfoProvider = EnsureArg.IsNotNull(modelInfoProvider, nameof(modelInfoProvider));
            _requestContextAccessor = EnsureArg.IsNotNull(requestContextAccessor, nameof(requestContextAccessor));
            _sqlQueueClient = EnsureArg.IsNotNull(sqlQueueClient, nameof(sqlQueueClient));

            _memoryStreamManager = new RecyclableMemoryStreamManager();

            lock (_watchDogLocker)
            {
                if (_defrag == null)
                {
                    _defrag = new Operations.DefragWorker(_sqlConnectionWrapperFactory, _schemaInformation, _sqlQueueClient);
                    ////_defrag.Start();
                }
            }
        }

        public async Task<UpsertOutcome> UpsertAsync(
            ResourceWrapper resource,
            WeakETag weakETag,
            bool allowCreate,
            bool keepHistory,
            CancellationToken cancellationToken,
            bool requireETagOnUpdate = false)
        {
            int? eTag = weakETag == null
                ? null
                : (int.TryParse(weakETag.VersionId, out var parsedETag) ? parsedETag : -1); // Set the etag to a sentinel value to enable expected failure paths when updating with both existing and nonexistent resources.

            var resourceMetadata = new ResourceMetadata(
                resource.CompartmentIndices,
                resource.SearchIndices?.ToLookup(e => _searchParameterTypeMap.GetSearchValueType(e)),
                resource.LastModifiedClaims);

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // ** We must use CreateNonRetrySqlCommand here because the retry will not reset the Stream containing the RawResource, resulting in a failure to save the data
                using (SqlConnectionWrapper sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true))
                using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateNonRetrySqlCommand())
                using (var stream = new RecyclableMemoryStream(_memoryStreamManager))
                {
                    // Read the latest resource
                    var existingResource = await GetAsync(new ResourceKey(resource.ResourceTypeName, resource.ResourceId), cancellationToken);

                    // Check for any validation errors
                    if (existingResource != null && eTag.HasValue && eTag.ToString() != existingResource.Version)
                    {
                        if (weakETag != null)
                        {
                            // The backwards compatibility behavior of Stu3 is to return 409 Conflict instead of a 412 Precondition Failed
                            if (_modelInfoProvider.Version == FhirSpecification.Stu3)
                            {
                                throw new ResourceConflictException(weakETag);
                            }

                            throw new PreconditionFailedException(string.Format(Core.Resources.ResourceVersionConflict, weakETag.VersionId));
                        }
                    }

                    int? existingVersion = null;

                    // There is no previous version of this resource, check validations and then simply call SP to create new version
                    if (existingResource == null)
                    {
                        if (resource.IsDeleted)
                        {
                            // Don't bother marking the resource as deleted since it already does not exist.
                            return null;
                        }

                        if (eTag.HasValue && eTag != null)
                        {
                            // You can't update a resource with a specified version if the resource does not exist
                            if (weakETag != null)
                            {
                                throw new ResourceNotFoundException(string.Format(Core.Resources.ResourceNotFoundByIdAndVersion, resource.ResourceTypeName, resource.ResourceId, weakETag.VersionId));
                            }
                        }

                        if (!allowCreate)
                        {
                            throw new MethodNotAllowedException(Core.Resources.ResourceCreationNotAllowed);
                        }
                    }
                    else
                    {
                        if (requireETagOnUpdate && !eTag.HasValue)
                        {
                            // This is a versioned update and no version was specified
                            // TODO: Add this to SQL error codes in AB#88286
                            // The backwards compatibility behavior of Stu3 is to return 412 Precondition Failed instead of a 400 Bad Request
                            if (_modelInfoProvider.Version == FhirSpecification.Stu3)
                            {
                                throw new PreconditionFailedException(string.Format(Core.Resources.IfMatchHeaderRequiredForResource, resource.ResourceTypeName));
                            }

                            throw new BadRequestException(string.Format(Core.Resources.IfMatchHeaderRequiredForResource, resource.ResourceTypeName));
                        }

                        if (resource.IsDeleted && existingResource.IsDeleted)
                        {
                            // Already deleted - don't create a new version
                            return null;
                        }

                        // check if reosurces are equal if its not a Delete action
                        if (!resource.IsDeleted)
                        {
                            // check if the new resource data is same as existing resource data
                            if (string.Equals(RemoveVersionIdAndLastUpdatedFromMeta(existingResource), RemoveVersionIdAndLastUpdatedFromMeta(resource), StringComparison.Ordinal))
                            {
                                // Send the existing resource in the response
                                return new UpsertOutcome(existingResource, SaveOutcomeType.Updated);
                            }
                        }

                        // existing version in the SQL db should never be null
                        existingVersion = int.Parse(existingResource.Version);
                        resource.Version = (existingVersion + 1).Value.ToString(CultureInfo.InvariantCulture);
                    }

                    _compressedRawResourceConverter.WriteCompressedRawResource(stream, resource.RawResource.Data);

                    stream.Seek(0, 0);

                    _logger.LogInformation("Upserting {ResourceTypeName} with a stream length of {StreamLength}", resource.ResourceTypeName, stream.Length);

                    // throwing ServiceUnavailableException in order to send a 503 error message to the client
                    // indicating the server has a transient error, and the client can try again
                    if (stream.Length < 31) // rather than error on a length of 0, a stream with a small number of bytes should still throw an error. RawResource.Data = null, still results in a stream of 29 bytes
                    {
                        _logger.LogCritical("Stream size for resource of type: {ResourceTypeName} is less than 50 bytes, request method: {RequestMethod}", resource.ResourceTypeName, resource.Request.Method);
                        throw new ServiceUnavailableException();
                    }

                    PopulateUpsertResourceCommand(sqlCommandWrapper, resource, resourceMetadata, allowCreate, keepHistory, requireETagOnUpdate, eTag, existingVersion, stream, _coreFeatures.SupportsResourceChangeCapture);

                    try
                    {
                        var newVersion = (int?)await sqlCommandWrapper.ExecuteScalarAsync(cancellationToken);
                        if (newVersion == null)
                        {
                            // indicates a redundant delete
                            return null;
                        }

                        if (newVersion == -1)
                        {
                            // indicates that resource content is same - no new version was created
                            // We need to send the existing resource in the response matching the correct versionId and lastUpdated as the one stored in DB
                            return new UpsertOutcome(existingResource, SaveOutcomeType.Updated);
                        }

                        resource.Version = newVersion.ToString();

                        SaveOutcomeType saveOutcomeType;
                        if (newVersion == 1)
                        {
                            saveOutcomeType = SaveOutcomeType.Created;
                        }
                        else
                        {
                            saveOutcomeType = SaveOutcomeType.Updated;
                            resource.RawResource.IsMetaSet = false;
                        }

                        return new UpsertOutcome(resource, saveOutcomeType);
                    }
                    catch (SqlException e)
                    {
                        switch (e.Number)
                        {
                            case SqlErrorCodes.Conflict:
                                // someone else beat us to it, re-read and try comparing again - Compared resource was updated
                                continue;
                            default:
                                _logger.LogError(e, "Error from SQL database on upsert");
                                throw;
                        }
                    }
                }
            }
        }

        private void PopulateUpsertResourceCommand(
            SqlCommandWrapper sqlCommandWrapper,
            ResourceWrapper resource,
            ResourceMetadata resourceMetadata,
            bool allowCreate,
            bool keepHistory,
            bool requireETagOnUpdate,
            int? eTag,
            int? comparedVersion,
            RecyclableMemoryStream stream,
            bool isResourceChangeCaptureEnabled)
        {
            long baseResourceSurrogateId = ResourceSurrogateIdHelper.LastUpdatedToResourceSurrogateId(resource.LastModified.UtcDateTime);
            short resourceTypeId = _model.GetResourceTypeId(resource.ResourceTypeName);

            // NOTE: in the following code a call to autogenerated method _upsertResourceTvpGeneratorV[Version].Generate() then appears to be making a
            // number of calls to interface ITableValuedParameterRowGenerator.GenerateRows() methods. But GenerateRows() contains "yield return"
            // statements, so GenerateRows() body is not really executed at this point and no rows are generated, only IEnumerable interface is returned.
            // This comment is put in here instead of in _upsertResourceTvpGeneratorV[Version].Generate() because
            // _upsertResourceTvpGeneratorV[Version].Generate() is autogenerated by a tool so we cannot put comment there.
            if (_schemaInformation.Current >= SchemaVersionConstants.TokenOverflow)
            {
                VLatest.UpsertResource.PopulateCommand(
                    sqlCommandWrapper,
                    baseResourceSurrogateId: baseResourceSurrogateId,
                    resourceTypeId: resourceTypeId,
                    resourceId: resource.ResourceId,
                    eTag: eTag,
                    allowCreate: allowCreate,
                    isDeleted: resource.IsDeleted,
                    keepHistory: keepHistory,
                    requireETagOnUpdate: requireETagOnUpdate,
                    requestMethod: resource.Request.Method,
                    searchParamHash: resource.SearchParameterHash,
                    rawResource: stream,
                    tableValuedParameters: _upsertResourceTvpGeneratorVLatest.Generate(new List<ResourceWrapper> { resource }),
                    isResourceChangeCaptureEnabled: isResourceChangeCaptureEnabled,
                    comparedVersion: comparedVersion);
            }
            else if (_schemaInformation.Current >= SchemaVersionConstants.PreventUpdatesFromCreatingVersionWhenNoImpact)
            {
                V40.UpsertResource.PopulateCommand(
                    sqlCommandWrapper,
                    baseResourceSurrogateId: baseResourceSurrogateId,
                    resourceTypeId: resourceTypeId,
                    resourceId: resource.ResourceId,
                    eTag: eTag,
                    allowCreate: allowCreate,
                    isDeleted: resource.IsDeleted,
                    keepHistory: keepHistory,
                    requireETagOnUpdate: requireETagOnUpdate,
                    requestMethod: resource.Request.Method,
                    searchParamHash: resource.SearchParameterHash,
                    rawResource: stream,
                    tableValuedParameters: _upsertResourceTvpGeneratorV40.Generate(new List<ResourceWrapper> { resource }),
                    isResourceChangeCaptureEnabled: isResourceChangeCaptureEnabled,
                    comparedVersion: comparedVersion);
            }
            else if (_schemaInformation.Current >= SchemaVersionConstants.PutCreateWithVersionedUpdatePolicyVersion)
            {
                V27.UpsertResource.PopulateCommand(
                    sqlCommandWrapper,
                    baseResourceSurrogateId: baseResourceSurrogateId,
                    resourceTypeId: resourceTypeId,
                    resourceId: resource.ResourceId,
                    eTag: eTag,
                    allowCreate: allowCreate,
                    isDeleted: resource.IsDeleted,
                    keepHistory: keepHistory,
                    requireETagOnUpdate: requireETagOnUpdate,
                    requestMethod: resource.Request.Method,
                    searchParamHash: resource.SearchParameterHash,
                    rawResource: stream,
                    tableValuedParameters: _upsertResourceTvpGeneratorV27.Generate(new List<ResourceWrapper> { resource }),
                    isResourceChangeCaptureEnabled: isResourceChangeCaptureEnabled);
            }
            else if (_schemaInformation.Current >= SchemaVersionConstants.AddMinMaxForDateAndStringSearchParamVersion)
            {
                V18.UpsertResource.PopulateCommand(
                    sqlCommandWrapper,
                    baseResourceSurrogateId: baseResourceSurrogateId,
                    resourceTypeId: resourceTypeId,
                    resourceId: resource.ResourceId,
                    eTag: eTag,
                    allowCreate: allowCreate,
                    isDeleted: resource.IsDeleted,
                    keepHistory: keepHistory,
                    requestMethod: resource.Request.Method,
                    searchParamHash: resource.SearchParameterHash,
                    rawResource: stream,
                    tableValuedParameters: _upsertResourceTvpGeneratorV18.Generate(new List<ResourceWrapper> { resource }),
                    isResourceChangeCaptureEnabled: isResourceChangeCaptureEnabled);
            }
            else if (_schemaInformation.Current >= SchemaVersionConstants.SupportsResourceChangeCaptureSchemaVersion)
            {
                V17.UpsertResource.PopulateCommand(
                    sqlCommandWrapper,
                    baseResourceSurrogateId: baseResourceSurrogateId,
                    resourceTypeId: resourceTypeId,
                    resourceId: resource.ResourceId,
                    eTag: eTag,
                    allowCreate: allowCreate,
                    isDeleted: resource.IsDeleted,
                    keepHistory: keepHistory,
                    requestMethod: resource.Request.Method,
                    searchParamHash: resource.SearchParameterHash,
                    rawResource: stream,
                    tableValuedParameters: _upsertResourceTvpGeneratorV17.Generate(new List<ResourceWrapper> { resource }),
                    isResourceChangeCaptureEnabled: isResourceChangeCaptureEnabled);
            }
            else if (_schemaInformation.Current >= SchemaVersionConstants.SearchParameterHashSchemaVersion)
            {
                V13.UpsertResource.PopulateCommand(
                    sqlCommandWrapper,
                    baseResourceSurrogateId: baseResourceSurrogateId,
                    resourceTypeId: resourceTypeId,
                    resourceId: resource.ResourceId,
                    eTag: eTag,
                    allowCreate: allowCreate,
                    isDeleted: resource.IsDeleted,
                    keepHistory: keepHistory,
                    requestMethod: resource.Request.Method,
                    searchParamHash: resource.SearchParameterHash,
                    rawResource: stream,
                    tableValuedParameters: _upsertResourceTvpGeneratorV13.Generate(new List<ResourceWrapper> { resource }));
            }
            else if (_schemaInformation.Current >= SchemaVersionConstants.SupportForReferencesWithMissingTypeVersion)
            {
                V7.UpsertResource.PopulateCommand(
                    sqlCommandWrapper,
                    baseResourceSurrogateId: baseResourceSurrogateId,
                    resourceTypeId: resourceTypeId,
                    resourceId: resource.ResourceId,
                    eTag: eTag,
                    allowCreate: allowCreate,
                    isDeleted: resource.IsDeleted,
                    keepHistory: keepHistory,
                    requestMethod: resource.Request.Method,
                    rawResource: stream,
                    tableValuedParameters: _upsertResourceTvpGeneratorV7.Generate(resourceMetadata));
            }
            else
            {
                V6.UpsertResource.PopulateCommand(
                    sqlCommandWrapper,
                    baseResourceSurrogateId: baseResourceSurrogateId,
                    resourceTypeId: resourceTypeId,
                    resourceId: resource.ResourceId,
                    eTag: eTag,
                    allowCreate: allowCreate,
                    isDeleted: resource.IsDeleted,
                    keepHistory: keepHistory,
                    requestMethod: resource.Request.Method,
                    rawResource: stream,
                    tableValuedParameters: _upsertResourceTvpGeneratorV6.Generate(resourceMetadata));
            }
        }

        public async Task<ResourceWrapper> GetAsync(ResourceKey key, CancellationToken cancellationToken)
        {
            using (SqlConnectionWrapper sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true))
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

                using (SqlCommandWrapper commandWrapper = sqlConnectionWrapper.CreateRetrySqlCommand())
                {
                    VLatest.ReadResource.PopulateCommand(
                        commandWrapper,
                        resourceTypeId: _model.GetResourceTypeId(key.ResourceType),
                        resourceId: key.Id,
                        version: requestedVersion);

                    using (SqlDataReader sqlDataReader = await commandWrapper.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken))
                    {
                        if (!await sqlDataReader.ReadAsync(cancellationToken))
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
                            rawResource = _compressedRawResourceConverter.ReadCompressedRawResource(rawResourceStream);
                        }

                        if (string.IsNullOrEmpty(rawResource))
                        {
                            rawResource = MissingResourceFactory.CreateJson(key.Id, key.ResourceType, "error", "exception");
                            _requestContextAccessor.SetMissingResourceCode(System.Net.HttpStatusCode.InternalServerError);
                        }

                        _logger.LogInformation("{NameOfResourceSurrogateId}: {ResourceSurrogateId}; {NameOfResourceType}: {ResourceType}; {NameOfRawResource} length: {RawResourceLength}", nameof(resourceSurrogateId), resourceSurrogateId, nameof(key.ResourceType), key.ResourceType, nameof(rawResource), rawResource.Length);

                        var isRawResourceMetaSet = sqlDataReader.Read(resourceTable.IsRawResourceMetaSet, 5);

                        string searchParamHash = null;

                        if (_schemaInformation.Current >= SchemaVersionConstants.SearchParameterHashSchemaVersion)
                        {
                            searchParamHash = sqlDataReader.Read(resourceTable.SearchParamHash, 6);
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
                            lastModifiedClaims: null,
                            searchParamHash)
                        {
                            IsHistory = isHistory,
                        };
                    }
                }
            }
        }

        public async Task HardDeleteAsync(ResourceKey key, bool keepCurrentVersion, CancellationToken cancellationToken)
        {
            using (SqlConnectionWrapper sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true))
            using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateRetrySqlCommand())
            {
                if (_schemaInformation.Current >= SchemaVersionConstants.PurgeHistoryVersion)
                {
                    VLatest.HardDeleteResource.PopulateCommand(sqlCommandWrapper, resourceTypeId: _model.GetResourceTypeId(key.ResourceType), resourceId: key.Id, Convert.ToInt16(keepCurrentVersion));
                }
                else if (!keepCurrentVersion)
                {
                    V12.HardDeleteResource.PopulateCommand(sqlCommandWrapper, resourceTypeId: _model.GetResourceTypeId(key.ResourceType), resourceId: key.Id);
                }
                else
                {
                    throw new BadRequestException(Resources.SchemaVersionNeedsToBeUpgraded);
                }

                await sqlCommandWrapper.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        public async Task BulkUpdateSearchParameterIndicesAsync(IReadOnlyCollection<ResourceWrapper> resources, CancellationToken cancellationToken)
        {
            using (SqlConnectionWrapper sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true))
            using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateRetrySqlCommand())
            {
                if (_schemaInformation.Current >= SchemaVersionConstants.AddMinMaxForDateAndStringSearchParamVersion)
                {
                    VLatest.BulkReindexResources.PopulateCommand(
                        sqlCommandWrapper,
                        _bulkReindexResourcesTvpGeneratorVLatest.Generate(resources.ToList()));
                }
                else
                {
                    V17.BulkReindexResources.PopulateCommand(
                        sqlCommandWrapper,
                        _bulkReindexResourcesTvpGeneratorV17.Generate(resources.ToList()));
                }

                if (_schemaInformation.Current >= SchemaVersionConstants.BulkReindexReturnsFailuresVersion)
                {
                    // We will reindex the rest of the batch if one resource has a versioning conflict
                    int? failedResourceCount;
                    try
                    {
                        failedResourceCount = (int?)await sqlCommandWrapper.ExecuteScalarAsync(cancellationToken);
                    }
                    catch (SqlException e)
                    {
                        _logger.LogError(e, "Error from SQL database on reindex.");
                        throw;
                    }

                    if (failedResourceCount != 0)
                    {
                        string message = string.Format(Core.Resources.ReindexingResourceVersionConflictWithCount, failedResourceCount);
                        string userAction = Core.Resources.ReindexingUserAction;
                        _logger.LogError("{Error}", message);
                        throw new PreconditionFailedException(message + " " + userAction);
                    }
                }
                else
                {
                    try
                    {
                        // We are running an earlier schema version where we will fail the whole batch if there is a conflict
                        await sqlCommandWrapper.ExecuteNonQueryAsync(cancellationToken);
                    }
                    catch (SqlException e)
                    {
                        switch (e.Number)
                        {
                            case SqlErrorCodes.PreconditionFailed:
                                string message = Core.Resources.ReindexingResourceVersionConflict;
                                string userAction = Core.Resources.ReindexingUserAction;

                                _logger.LogError("{Error}", message);
                                throw new PreconditionFailedException(message + " " + userAction);

                            default:
                                _logger.LogError(e, "Error from SQL database on reindex.");
                                throw;
                        }
                    }
                }
            }
        }

        private static string RemoveTrailingZerosFromMillisecondsForAGivenDate(DateTimeOffset date)
        {
            // 0000000+ -> +, 0010000+ -> 001+, 0100000+ -> 01+, 0180000+ -> 018+, 1000000 -> 1+, 1100000+ -> 11+, 1010000+ -> 101+
            // ToString("o") - Formats to 2022-03-09T01:40:52.0690000+02:00 but serialized value to string in dB is 2022-03-09T01:40:52.069+02:00
            var formattedDate = date.ToString("o", CultureInfo.InvariantCulture);
            var milliseconds = formattedDate.Substring(20, 7); // get 0690000
            var trimmedMilliseconds = milliseconds.TrimEnd('0'); // get 069
            if (milliseconds.Equals("0000000", StringComparison.Ordinal))
            {
                // when date = 2022-03-09T01:40:52.0000000+02:00, value in dB is 2022-03-09T01:40:52+02:00, we need to replace the . after second
                return formattedDate.Replace("." + milliseconds, string.Empty, StringComparison.Ordinal);
            }

            return formattedDate.Replace(milliseconds, trimmedMilliseconds, StringComparison.Ordinal);
        }

        private static string RemoveVersionIdAndLastUpdatedFromMeta(ResourceWrapper resourceWrapper)
        {
            var versionToReplace = resourceWrapper.RawResource.IsMetaSet ? resourceWrapper.Version : "1";
            var rawResource = resourceWrapper.RawResource.Data.Replace($"\"versionId\":\"{versionToReplace}\"", string.Empty, StringComparison.Ordinal);
            return rawResource.Replace($"\"lastUpdated\":\"{RemoveTrailingZerosFromMillisecondsForAGivenDate(resourceWrapper.LastModified)}\"", string.Empty, StringComparison.Ordinal);
        }

        public void Build(ICapabilityStatementBuilder builder)
        {
            EnsureArg.IsNotNull(builder, nameof(builder));

            builder.PopulateDefaultResourceInteractions()
                .SyncSearchParameters()
                .AddGlobalSearchParameters()
                .SyncProfiles();

            if (_coreFeatures.SupportsBatch)
            {
                // Batch supported added in listedCapability
                builder.AddGlobalInteraction(SystemRestfulInteraction.Batch);
            }

            if (_coreFeatures.SupportsTransaction)
            {
                // Transaction supported added in listedCapability
                builder.AddGlobalInteraction(SystemRestfulInteraction.Transaction);
            }
        }

        public async Task<ResourceWrapper> UpdateSearchParameterIndicesAsync(ResourceWrapper resource, WeakETag weakETag, CancellationToken cancellationToken)
        {
            int? eTag = weakETag == null
                ? null
                : (int.TryParse(weakETag.VersionId, out var parsedETag) ? parsedETag : -1); // Set the etag to a sentinel value to enable expected failure paths when updating with both existing and nonexistent resources.

            using (SqlConnectionWrapper sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true))
            using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateRetrySqlCommand())
            {
                if (_schemaInformation.Current >= SchemaVersionConstants.AddMinMaxForDateAndStringSearchParamVersion)
                {
                    VLatest.ReindexResource.PopulateCommand(
                        sqlCommandWrapper,
                        resourceTypeId: _model.GetResourceTypeId(resource.ResourceTypeName),
                        resourceId: resource.ResourceId,
                        eTag,
                        searchParamHash: resource.SearchParameterHash,
                        tableValuedParameters: _reindexResourceTvpGeneratorVLatest.Generate(new List<ResourceWrapper> { resource }));
                }
                else
                {
                    V17.ReindexResource.PopulateCommand(
                        sqlCommandWrapper,
                        resourceTypeId: _model.GetResourceTypeId(resource.ResourceTypeName),
                        resourceId: resource.ResourceId,
                        eTag,
                        searchParamHash: resource.SearchParameterHash,
                        tableValuedParameters: _reindexResourceTvpGeneratorV17.Generate(new List<ResourceWrapper> { resource }));
                }

                try
                {
                    await sqlCommandWrapper.ExecuteScalarAsync(cancellationToken);

                    return resource;
                }
                catch (SqlException e)
                {
                    switch (e.Number)
                    {
                        case SqlErrorCodes.PreconditionFailed:
                            _logger.LogError(string.Format(Core.Resources.ResourceVersionConflict, weakETag));
                            throw new PreconditionFailedException(string.Format(Core.Resources.ResourceVersionConflict, weakETag));

                        default:
                            _logger.LogError(e, "Error from SQL database on reindex.");
                            throw;
                    }
                }
            }
        }

        public async Task<int?> GetProvisionedDataStoreCapacityAsync(CancellationToken cancellationToken = default)
        {
            return await Task.FromResult((int?)null);
        }
    }
}
