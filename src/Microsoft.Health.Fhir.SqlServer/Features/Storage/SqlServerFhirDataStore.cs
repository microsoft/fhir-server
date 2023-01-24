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
using AngleSharp.Common;
using EnsureThat;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
        private readonly VLatest.UpsertResourceTvpGenerator<IReadOnlyList<ResourceWrapper>> _upsertResourceTvpGeneratorVLatest;
        private readonly VLatest.MergeResourcesTvpGenerator<IReadOnlyList<MergeResourceWrapper>> _mergeResourcesTvpGeneratorVLatest;
        private readonly VLatest.ReindexResourceTvpGenerator<IReadOnlyList<ResourceWrapper>> _reindexResourceTvpGeneratorVLatest;
        private readonly VLatest.BulkReindexResourcesTvpGenerator<IReadOnlyList<ResourceWrapper>> _bulkReindexResourcesTvpGeneratorVLatest;
        private readonly RecyclableMemoryStreamManager _memoryStreamManager;
        private readonly CoreFeatureConfiguration _coreFeatures;
        private readonly SqlConnectionWrapperFactory _sqlConnectionWrapperFactory;
        private readonly ICompressedRawResourceConverter _compressedRawResourceConverter;
        private readonly ILogger<SqlServerFhirDataStore> _logger;
        private readonly SchemaInformation _schemaInformation;
        private readonly IModelInfoProvider _modelInfoProvider;
        private readonly Lazy<IConformanceProvider> _conformanceProvider;
        private const string InitialVersion = "1";

        public SqlServerFhirDataStore(
            ISqlServerFhirModel model,
            SearchParameterToSearchValueTypeMap searchParameterTypeMap,
            VLatest.UpsertResourceTvpGenerator<IReadOnlyList<ResourceWrapper>> upsertResourceTvpGeneratorVLatest,
            VLatest.MergeResourcesTvpGenerator<IReadOnlyList<MergeResourceWrapper>> mergeResourcesTvpGeneratorVLatest,
            VLatest.ReindexResourceTvpGenerator<IReadOnlyList<ResourceWrapper>> reindexResourceTvpGeneratorVLatest,
            VLatest.BulkReindexResourcesTvpGenerator<IReadOnlyList<ResourceWrapper>> bulkReindexResourcesTvpGeneratorVLatest,
            IOptions<CoreFeatureConfiguration> coreFeatures,
            SqlConnectionWrapperFactory sqlConnectionWrapperFactory,
            ICompressedRawResourceConverter compressedRawResourceConverter,
            ILogger<SqlServerFhirDataStore> logger,
            SchemaInformation schemaInformation,
            IModelInfoProvider modelInfoProvider,
            RequestContextAccessor<IFhirRequestContext> requestContextAccessor,
            Lazy<IConformanceProvider> conformanceProvider)
        {
            _model = EnsureArg.IsNotNull(model, nameof(model));
            _searchParameterTypeMap = EnsureArg.IsNotNull(searchParameterTypeMap, nameof(searchParameterTypeMap));
            _upsertResourceTvpGeneratorVLatest = EnsureArg.IsNotNull(upsertResourceTvpGeneratorVLatest, nameof(upsertResourceTvpGeneratorVLatest));
            _mergeResourcesTvpGeneratorVLatest = EnsureArg.IsNotNull(mergeResourcesTvpGeneratorVLatest, nameof(mergeResourcesTvpGeneratorVLatest));
            _reindexResourceTvpGeneratorVLatest = EnsureArg.IsNotNull(reindexResourceTvpGeneratorVLatest, nameof(reindexResourceTvpGeneratorVLatest));
            _bulkReindexResourcesTvpGeneratorVLatest = EnsureArg.IsNotNull(bulkReindexResourcesTvpGeneratorVLatest, nameof(bulkReindexResourcesTvpGeneratorVLatest));
            _coreFeatures = EnsureArg.IsNotNull(coreFeatures?.Value, nameof(coreFeatures));
            _sqlConnectionWrapperFactory = EnsureArg.IsNotNull(sqlConnectionWrapperFactory, nameof(sqlConnectionWrapperFactory));
            _compressedRawResourceConverter = EnsureArg.IsNotNull(compressedRawResourceConverter, nameof(compressedRawResourceConverter));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
            _schemaInformation = EnsureArg.IsNotNull(schemaInformation, nameof(schemaInformation));
            _modelInfoProvider = EnsureArg.IsNotNull(modelInfoProvider, nameof(modelInfoProvider));
            _requestContextAccessor = EnsureArg.IsNotNull(requestContextAccessor, nameof(requestContextAccessor));
            _conformanceProvider = EnsureArg.IsNotNull(conformanceProvider, nameof(conformanceProvider));

            _memoryStreamManager = new RecyclableMemoryStreamManager();
        }

        public async Task<IDictionary<ResourceKey, UpsertOutcome>> MergeAsync(IReadOnlyList<ResourceWrapper> resources, CancellationToken cancellationToken)
        {
            var results = new Dictionary<ResourceKey, UpsertOutcome>();
            if (resources == null || resources.Count == 0)
            {
                return results;
            }

            var retries = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var existingResources = (await GetAsync(resources.Select(_ => _.ToResourceKey()).ToList(), cancellationToken)).ToDictionary(_ => _.ToResourceKey(), _ => _);

                    // assume that most likely case is that all resources should be updated
                    var minSurrId = (await MergeResourcesBeginTransactionAsync(resources.Count, cancellationToken)).MinResourceSurrogateId;

                    var index = 0;
                    var mergeWrappers = new List<MergeResourceWrapper>();
                    foreach (var resource in resources)
                    {
                        var resourceKey = resource.ToResourceKey();
                        if (existingResources.TryGetValue(resourceKey, out var existingResource))
                        {
                            if ((resource.IsDeleted && existingResource.IsDeleted) || ExistingRawResourceIsEqualToInput(resource, existingResource))
                            {
                                results.Add(resourceKey, new UpsertOutcome(existingResource, SaveOutcomeType.Updated));
                                continue;
                            }

                            resource.Version = (int.Parse(existingResource.Version) + 1).ToString(CultureInfo.InvariantCulture);
                        }

                        var surrId = minSurrId + index;
                        resource.LastModified = new DateTimeOffset(ResourceSurrogateIdHelper.ResourceSurrogateIdToLastUpdated(surrId), TimeSpan.Zero);
                        ReplaceVersionIdAndLastUpdatedInMeta(resource);
                        mergeWrappers.Add(new MergeResourceWrapper(resource, surrId, true, true));
                        index++;
                        results.Add(resourceKey, new UpsertOutcome(resource, resource.Version == InitialVersion ? SaveOutcomeType.Created : SaveOutcomeType.Updated));
                    }

                    using var conn = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, false);
                    using var cmd = conn.CreateNonRetrySqlCommand();
                    VLatest.MergeResources.PopulateCommand(
                        cmd,
                        AffectedRows: 0,
                        RaiseExceptionOnConflict: true,
                        IsResourceChangeCaptureEnabled: _coreFeatures.SupportsResourceChangeCapture,
                        tableValuedParameters: _mergeResourcesTvpGeneratorVLatest.Generate(mergeWrappers));
                    await cmd.ExecuteNonQueryAsync(cancellationToken);

                    return results;
                }
                catch (SqlException e)
                {
                    if ((e.Number == SqlErrorCodes.Conflict || e.IsRetryable()) && retries++ < 10) // retries on conflict should never be more than 1, so it is OK to hardcode.
                    {
                        _logger.LogWarning(e, $"Error from SQL database on {nameof(MergeAsync)}");
                        await Task.Delay(5000);
                        continue;
                    }

                    _logger.LogError(e, $"Error from SQL database on {nameof(MergeAsync)} retries={retries}");
                    throw;
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

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

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

                    resource.Version = InitialVersion;
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
                        if (ExistingRawResourceIsEqualToInput(resource, existingResource))
                        {
                            // Send the existing resource in the response
                            return new UpsertOutcome(existingResource, SaveOutcomeType.Updated);
                        }
                    }

                    // existing version in the SQL db should never be null
                    existingVersion = int.Parse(existingResource.Version);
                    resource.Version = (existingVersion + 1).Value.ToString(CultureInfo.InvariantCulture);
                }

                try
                {
                    // ** We must use CreateNonRetrySqlCommand here because the retry will not reset the Stream containing the RawResource, resulting in a failure to save the data
                    using var sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true);
                    using var sqlCommandWrapper = sqlConnectionWrapper.CreateNonRetrySqlCommand();
                    if (_schemaInformation.Current >= SchemaVersionConstants.Merge)
                    {
                        var surrId = (await MergeResourcesBeginTransactionAsync(1, cancellationToken)).MinResourceSurrogateId;

                        resource.LastModified = new DateTimeOffset(ResourceSurrogateIdHelper.ResourceSurrogateIdToLastUpdated(surrId), TimeSpan.Zero);
                        ReplaceVersionIdAndLastUpdatedInMeta(resource);

                        VLatest.MergeResources.PopulateCommand(
                            sqlCommandWrapper,
                            AffectedRows: 0,
                            RaiseExceptionOnConflict: true,
                            IsResourceChangeCaptureEnabled: _coreFeatures.SupportsResourceChangeCapture,
                            tableValuedParameters: _mergeResourcesTvpGeneratorVLatest.Generate(new List<MergeResourceWrapper> { new MergeResourceWrapper(resource, surrId, keepHistory, true) }));
                        await sqlCommandWrapper.ExecuteNonQueryAsync(cancellationToken);
                    }
                    else
                    {
                        using var stream = new RecyclableMemoryStream(_memoryStreamManager, tag: nameof(SqlServerFhirDataStore));
                        _compressedRawResourceConverter.WriteCompressedRawResource(stream, resource.RawResource.Data);
                        stream.Seek(0, 0);
                        PopulateUpsertResourceCommand(sqlCommandWrapper, resource, keepHistory, existingVersion, stream, _coreFeatures.SupportsResourceChangeCapture);
                        await sqlCommandWrapper.ExecuteNonQueryAsync(cancellationToken); // resource.Version has been set already
                        resource.RawResource.IsMetaSet = resource.Version == InitialVersion;
                    }

                    return new UpsertOutcome(resource, resource.Version == InitialVersion ? SaveOutcomeType.Created : SaveOutcomeType.Updated);
                }
                catch (SqlException e)
                {
                    _logger.LogError(e, $"Error from SQL database on {nameof(UpsertAsync)}");

                    switch (e.Number)
                    {
                        case SqlErrorCodes.Conflict:
                            // someone else beat us to it, re-read and try comparing again - Compared resource was updated
                            continue;
                        default:
                            throw;
                    }
                }
            }
        }

        private void PopulateUpsertResourceCommand(
            SqlCommandWrapper sqlCommandWrapper,
            ResourceWrapper resource,
            bool keepHistory,
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
            VLatest.UpsertResource.PopulateCommand(
                sqlCommandWrapper,
                baseResourceSurrogateId: baseResourceSurrogateId,
                resourceTypeId: resourceTypeId,
                resourceId: resource.ResourceId,
                eTag: null, // not used in stored procedure
                allowCreate: true, // not used in stored procedure
                isDeleted: resource.IsDeleted,
                keepHistory: keepHistory,
                requireETagOnUpdate: true, // not used in stored procedure
                requestMethod: resource.Request.Method,
                searchParamHash: resource.SearchParameterHash,
                rawResource: stream,
                tableValuedParameters: _upsertResourceTvpGeneratorVLatest.Generate(new List<ResourceWrapper> { resource }),
                isResourceChangeCaptureEnabled: isResourceChangeCaptureEnabled,
                comparedVersion: comparedVersion);
        }

        public async Task<IReadOnlyList<ResourceWrapper>> GetAsync(IReadOnlyList<ResourceKey> keys, CancellationToken cancellationToken)
        {
            using var conn = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, false);
            using var cmd = conn.CreateRetrySqlCommand();
            VLatest.GetResources.PopulateCommand(cmd, keys.Select(_ => new ResourceKeyListRow(_model.GetResourceTypeId(_.ResourceType), _.Id, _.VersionId == null ? null : int.TryParse(_.VersionId, out var version) ? version : int.MinValue))); // put min value when cannot parse so resource will be not found

            using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken);
            var resources = new List<ResourceWrapper>();
            while (await reader.ReadAsync(cancellationToken))
            {
                var table = VLatest.Resource;
                var resourceTypeId = reader.Read(table.ResourceTypeId, 0);
                var resourceId = reader.Read(table.ResourceId, 1);
                var resourceSurrogateId = reader.Read(table.ResourceSurrogateId, 2);
                var version = reader.Read(table.Version, 3);
                var isDeleted = reader.Read(table.IsDeleted, 4);
                var isHistory = reader.Read(table.IsHistory, 5);
                var rawResourceBytes = reader.GetSqlBytes(6).Value;
                var isRawResourceMetaSet = reader.Read(table.IsRawResourceMetaSet, 7);
                var searchParamHash = reader.Read(table.SearchParamHash, 8);

                using var rawResourceStream = new MemoryStream(rawResourceBytes);
                var rawResource = _compressedRawResourceConverter.ReadCompressedRawResource(rawResourceStream);

                var resource = new ResourceWrapper(
                    resourceId,
                    version.ToString(CultureInfo.InvariantCulture),
                    _model.GetResourceTypeName(resourceTypeId),
                    new RawResource(rawResource, FhirResourceFormat.Json, isMetaSet: isRawResourceMetaSet),
                    null,
                    new DateTimeOffset(ResourceSurrogateIdHelper.ResourceSurrogateIdToLastUpdated(resourceSurrogateId), TimeSpan.Zero),
                    isDeleted,
                    searchIndices: null,
                    compartmentIndices: null,
                    lastModifiedClaims: null,
                    searchParameterHash: searchParamHash)
                {
                    IsHistory = isHistory,
                };

                resources.Add(resource);
            }

            await reader.NextResultAsync(cancellationToken);

            return resources;
        }

        public async Task<ResourceWrapper> GetAsync(ResourceKey key, CancellationToken cancellationToken)
        {
            // TODO: Remove if when Merge is released
            if (_schemaInformation.Current >= SchemaVersionConstants.Merge)
            {
                var results = await GetAsync(new List<ResourceKey> { key }, cancellationToken);
                return results.Count == 0 ? null : results[0];
            }

            // TODO: Remove all below when Merge is released
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
                VLatest.HardDeleteResource.PopulateCommand(sqlCommandWrapper, resourceTypeId: _model.GetResourceTypeId(key.ResourceType), resourceId: key.Id, Convert.ToInt16(keepCurrentVersion));
                await sqlCommandWrapper.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        public async Task BulkUpdateSearchParameterIndicesAsync(IReadOnlyCollection<ResourceWrapper> resources, CancellationToken cancellationToken)
        {
            using (SqlConnectionWrapper sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true))
            using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateRetrySqlCommand())
            {
                VLatest.BulkReindexResources.PopulateCommand(
                    sqlCommandWrapper,
                    _bulkReindexResourcesTvpGeneratorVLatest.Generate(resources.ToList()));

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

        private void ReplaceVersionIdAndLastUpdatedInMeta(ResourceWrapper resourceWrapper)
        {
            var date = GetJsonValue(resourceWrapper.RawResource.Data, "lastUpdated");
            string rawResourceData;
            if (resourceWrapper.Version == InitialVersion) // version is already correct
            {
                rawResourceData = resourceWrapper.RawResource.Data
                                    .Replace($"\"lastUpdated\":\"{date}\"", $"\"lastUpdated\":\"{RemoveTrailingZerosFromMillisecondsForAGivenDate(resourceWrapper.LastModified)}\"", StringComparison.Ordinal);
            }
            else
            {
                var version = GetJsonValue(resourceWrapper.RawResource.Data, "versionId");
                rawResourceData = resourceWrapper.RawResource.Data
                                    .Replace($"\"versionId\":\"{version}\"", $"\"versionId\":\"{resourceWrapper.Version}\"", StringComparison.Ordinal)
                                    .Replace($"\"lastUpdated\":\"{date}\"", $"\"lastUpdated\":\"{RemoveTrailingZerosFromMillisecondsForAGivenDate(resourceWrapper.LastModified)}\"", StringComparison.Ordinal);
            }

            resourceWrapper.RawResource = new RawResource(rawResourceData, FhirResourceFormat.Json, true);
        }

        private bool ExistingRawResourceIsEqualToInput(ResourceWrapper input, ResourceWrapper existing) // call is not symmetrical, it assumes version = 1 on input.
        {
            var inputDate = GetJsonValue(input.RawResource.Data, "lastUpdated");
            var existingDate = GetJsonValue(existing.RawResource.Data, "lastUpdated");
            var existingVersion = GetJsonValue(existing.RawResource.Data, "versionId");
            if (existingVersion != InitialVersion)
            {
                return input.RawResource.Data == existing.RawResource.Data.Replace($"\"lastUpdated\":\"{existingDate}\"", $"\"lastUpdated\":\"{inputDate}\"", StringComparison.Ordinal);
            }
            else
            {
                return input.RawResource.Data
                            == existing.RawResource.Data
                                    .Replace($"\"versionId\":\"{existingVersion}\"", $"\"versionId\":\"{InitialVersion}\"", StringComparison.Ordinal)
                                    .Replace($"\"lastUpdated\":\"{existingDate}\"", $"\"lastUpdated\":\"{inputDate}\"", StringComparison.Ordinal);
            }
        }

        // This method relies on current raw resource string formatting, i.e. no extra spaces.
        // This logic should be removed once "resource.meta not available" bug is fixed.
        private string GetJsonValue(string json, string propName)
        {
            var startIndex = json.IndexOf($"\"{propName}\":\"", StringComparison.Ordinal);
            if (startIndex == -1)
            {
                _logger.LogError($"Cannot parse {propName} from {json}");
                return string.Empty;
            }

            startIndex = startIndex + propName.Length + 4;
            var endIndex = json.IndexOf("\"", startIndex, StringComparison.Ordinal);
            if (endIndex == -1)
            {
                _logger.LogError($"Cannot parse {propName} value from {json}");
                return string.Empty;
            }

            var value = json.Substring(startIndex, endIndex - startIndex);

            return value;
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

        public async Task<(long TransactionId, long MinResourceSurrogateId)> MergeResourcesBeginTransactionAsync(int resourceVersionCount, CancellationToken cancellationToken)
        {
            using var conn = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, false);
            using var cmd = conn.CreateNonRetrySqlCommand();
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = "dbo.MergeResourcesBeginTransaction";
            cmd.Parameters.AddWithValue("@Count", resourceVersionCount);
            var surrogateIdParam = new SqlParameter("@MinResourceSurrogateId", SqlDbType.BigInt) { Direction = ParameterDirection.Output };
            cmd.Parameters.Add(surrogateIdParam);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
            var surrogateId = (long)surrogateIdParam.Value;
            return (surrogateId, surrogateId);
        }

        public async Task<ResourceWrapper> UpdateSearchParameterIndicesAsync(ResourceWrapper resource, WeakETag weakETag, CancellationToken cancellationToken)
        {
            int? eTag = weakETag == null
                ? null
                : (int.TryParse(weakETag.VersionId, out var parsedETag) ? parsedETag : -1); // Set the etag to a sentinel value to enable expected failure paths when updating with both existing and nonexistent resources.

            using (SqlConnectionWrapper sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true))
            using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateRetrySqlCommand())
            {
                VLatest.ReindexResource.PopulateCommand(
                    sqlCommandWrapper,
                    resourceTypeId: _model.GetResourceTypeId(resource.ResourceTypeName),
                    resourceId: resource.ResourceId,
                    eTag,
                    searchParamHash: resource.SearchParameterHash,
                    tableValuedParameters: _reindexResourceTvpGeneratorVLatest.Generate(new List<ResourceWrapper> { resource }));

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
