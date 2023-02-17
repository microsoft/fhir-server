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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Specialized;
using EnsureThat;
using Hl7.Fhir.Utility;
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
        private const string InitialVersion = "1";
        public const string MergeResourcesDisabledFlagId = "MergeResources.IsDisabled";
        private static MergeResourcesFeatureFlag _mergeResourcesFeatureFlag;
        private static object _mergeResourcesFeatureFlagLocker = new object();
        private static object _adlsContainerLocker = new object();
        private readonly string _adlsConnectionString;
        private readonly string _sqlConnectionString;
        private readonly string _adlsContainer;
        private static bool _saveResourcesToAdls = true;
        private static object _saveResourcesToAdlsLocker = new object();
        private readonly BlobContainerClient _adlsClient;

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
            IOptions<ExportJobConfiguration> adlsOptions,
            IOptions<SqlServerDataStoreConfiguration> sqlOptions)
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
            EnsureArg.IsNotNull(adlsOptions, nameof(adlsOptions));
            EnsureArg.IsNotNull(sqlOptions, nameof(sqlOptions));

            _memoryStreamManager = new RecyclableMemoryStreamManager();

            if (_mergeResourcesFeatureFlag == null)
            {
                lock (_mergeResourcesFeatureFlagLocker)
                {
                    _mergeResourcesFeatureFlag ??= new MergeResourcesFeatureFlag(_sqlConnectionWrapperFactory);
                }
            }

            _adlsConnectionString = adlsOptions.Value.StorageAccountConnection;
            _sqlConnectionString = sqlOptions.Value.ConnectionString;
            _adlsContainer = new SqlConnectionStringBuilder(_sqlConnectionString).InitialCatalog.Shorten(30).Replace("_", "-", StringComparison.InvariantCultureIgnoreCase).ToLowerInvariant();
            _adlsClient = GetAdlsContainer();
        }

        public async Task TryLogEvent(string process, string status, string text, DateTime startDate, CancellationToken cancellationToken)
        {
            try
            {
                using var conn = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, false);
                using var cmd = conn.CreateNonRetrySqlCommand();
                VLatest.LogEvent.PopulateCommand(cmd, process, status, null, null, null, null, startDate, text, null, null);
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }
            catch
            {
                // do nothing;
            }
        }

        private async Task TryLogEvent(string process, string status, string mode, int rows, string text, DateTime startDate, CancellationToken cancellationToken)
        {
            try
            {
                using var conn = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, false);
                using var cmd = conn.CreateNonRetrySqlCommand();
                VLatest.LogEvent.PopulateCommand(cmd, process, status, mode, null, null, rows, startDate, text, null, null);
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }
            catch
            {
                // do nothing;
            }
        }

        public async Task<IDictionary<ResourceKey, UpsertOutcome>> MergeAsync(IReadOnlyList<ResourceWrapperOperation> resources, CancellationToken cancellationToken)
        {
            var saveToAdls = SaveResourcesToAdls();
            var start = DateTime.UtcNow;
            var retries = 0;
            while (true)
            {
                var results = new Dictionary<ResourceKey, UpsertOutcome>();
                if (resources == null || resources.Count == 0)
                {
                    return results;
                }

                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    // ignore input resource version to get latest version from the store
                    var existingResources = (await GetAsync(resources.Select(r => r.Wrapper.ToResourceKey(true)).Distinct().ToList(), cancellationToken)).ToDictionary(r => r.ToResourceKey(true), r => r);

                    // assume that most likely case is that all resources should be updated
                    var minSurrId = await MergeResourcesBeginTransactionAsync(resources.Count, cancellationToken);

                    var index = 0;
                    var mergeWrappers = new List<MergeResourceWrapper>();
                    foreach (var resourceExt in resources)
                    {
                        var weakETag = resourceExt.WeakETag;
                        int? eTag = weakETag == null
                            ? null
                            : (int.TryParse(weakETag.VersionId, out var parsedETag) ? parsedETag : -1); // Set the etag to a sentinel value to enable expected failure paths when updating with both existing and nonexistent resources.

                        var resource = resourceExt.Wrapper;
                        var resourceKey = resource.ToResourceKey(); // keep input version in the results to allow processing multiple versions per resource
                        existingResources.TryGetValue(resource.ToResourceKey(true), out var existingResource);

                        // Check for any validation errors
                        if (existingResource != null && eTag.HasValue && !string.Equals(eTag.ToString(), existingResource.Version, StringComparison.Ordinal))
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

                        // There is no previous version of this resource, check validations and then simply call SP to create new version
                        if (existingResource == null)
                        {
                            if (resource.IsDeleted)
                            {
                                // Don't bother marking the resource as deleted since it already does not exist.
                                results.Add(resourceKey, null);
                                continue;
                            }

                            if (eTag.HasValue)
                            {
                                // You can't update a resource with a specified version if the resource does not exist
                                if (weakETag != null)
                                {
                                    throw new ResourceNotFoundException(string.Format(Core.Resources.ResourceNotFoundByIdAndVersion, resource.ResourceTypeName, resource.ResourceId, weakETag.VersionId));
                                }
                            }

                            if (!resourceExt.AllowCreate)
                            {
                                throw new MethodNotAllowedException(Core.Resources.ResourceCreationNotAllowed);
                            }

                            resource.Version = InitialVersion;
                        }
                        else
                        {
                            if (resourceExt.RequireETagOnUpdate && !eTag.HasValue)
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
                                results.Add(resourceKey, null);
                                continue;
                            }

                            // check if resources are equal if its not a Delete action
                            if (!resource.IsDeleted)
                            {
                                // check if the new resource data is same as existing resource data
                                if (ExistingRawResourceIsEqualToInput(resource, existingResource))
                                {
                                    // Send the existing resource in the response
                                    results.Add(resourceKey, new UpsertOutcome(existingResource, SaveOutcomeType.Updated));
                                    continue;
                                }
                            }

                            // existing version in the SQL db should never be null
                            resource.Version = (int.Parse(existingResource.Version) + 1).ToString(CultureInfo.InvariantCulture);
                        }

                        var surrId = minSurrId + index;
                        resource.LastModified = new DateTimeOffset(ResourceSurrogateIdHelper.ResourceSurrogateIdToLastUpdated(surrId), TimeSpan.Zero);
                        ReplaceVersionIdAndLastUpdatedInMeta(resource);
                        mergeWrappers.Add(new MergeResourceWrapper(resource, surrId, resourceExt.KeepHistory, true)); // TODO: When multiple versions for a resource are supported use correct value instead of last true.
                        index++;
                        results.Add(resourceKey, new UpsertOutcome(resource, resource.Version == InitialVersion ? SaveOutcomeType.Created : SaveOutcomeType.Updated));
                    }

                    if (mergeWrappers.Count > 0) // do not call db with empty input
                    {
                        if (saveToAdls)
                        {
                            // save to adls first
                            PutRawResourcesToAdls(mergeWrappers, minSurrId);
                        }

                        using var conn = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true); // TODO: Remove tran enlist when true bundle logic is in place.
                        using var cmd = conn.CreateNonRetrySqlCommand();
                        VLatest.MergeResources.PopulateCommand(
                            cmd,
                            AffectedRows: 0,
                            RaiseExceptionOnConflict: true,
                            IsResourceChangeCaptureEnabled: _coreFeatures.SupportsResourceChangeCapture,
                            tableValuedParameters: _mergeResourcesTvpGeneratorVLatest.Generate(mergeWrappers));
                        cmd.CommandTimeout = 180 + (int)(3600.0 / 10000 * mergeWrappers.Count);
                        await cmd.ExecuteNonQueryAsync(cancellationToken);
                    }

                    await MergeResourcesCommitTransactionAsync(minSurrId, cancellationToken);

                    await TryLogEvent("MergeAsync", "Warn", $"SaveToAdls={saveToAdls}", (int)(DateTime.UtcNow - start).TotalMilliseconds, $"ExistingResources={existingResources.Count} MergedResources={mergeWrappers.Count}", start, CancellationToken.None);
                    return results;
                }
                catch (SqlException e)
                {
                    // we cannot retry on connection loss as this call might be in outer transaction.
                    // TODO: Add retries when set bundle processing is in place.
                    if (e.Number == SqlErrorCodes.Conflict && retries++ < 10) // retries on conflict should never be more than 1, so it is OK to hardcode.
                    {
                        _logger.LogWarning(e, $"Error from SQL database on {nameof(MergeAsync)} retries={{Retries}}", retries);
                        await Task.Delay(5000, cancellationToken);
                        continue;
                    }

                    _logger.LogError(e, $"Error from SQL database on {nameof(MergeAsync)} retries={{Retries}}", retries);
                    throw;
                }
            }
        }

        private static bool SaveResourcesToAdls()
        {
            lock (_saveResourcesToAdlsLocker)
            {
                _saveResourcesToAdls = _saveResourcesToAdls ? false : true;
                return _saveResourcesToAdls;
            }
        }

        private static string GetBlobName(long transactionId)
        {
            return $"transaction-{transactionId}.tjson";
        }

        private void PutRawResourcesToAdls(IList<MergeResourceWrapper> resources, long transactionId)
        {
            var blobName = GetBlobName(transactionId);
            var eol = Encoding.UTF8.GetByteCount(Environment.NewLine);
        retry:
            try
            {
                using var stream = _adlsClient.GetBlockBlobClient(blobName).OpenWrite(true);
                using var writer = new StreamWriter(stream);
                var offset = 0;
                foreach (var resource in resources)
                {
                    resource.TransactionId = transactionId;
                    resource.OffsetInFile = offset;
                    var line = $"{resource.ResourceWrapper.ResourceTypeName}\t{resource.ResourceWrapper.ResourceId}\t{resource.ResourceWrapper.Version}\t{resource.ResourceWrapper.IsDeleted}\t{resource.ResourceWrapper.RawResource.Data}";
                    offset += Encoding.UTF8.GetByteCount(line) + eol;
                    writer.WriteLine(line);
                }

                writer.Flush();
            }
            catch (Exception e)
            {
                if (e.ToString().Contains("ConditionNotMet", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogError(e, $"Error writing to ADLS blob={{BlobName}}", blobName);
                    Console.WriteLine(e);
                    goto retry;
                }

                throw;
            }
        }

        public string GetRawResourceFromAdls(long transactionId, int offsetInFile)
        {
            var blobName = GetBlobName(transactionId);
            using var reader = new StreamReader(_adlsClient.GetBlobClient(blobName).OpenRead(offsetInFile));
            var line = reader.ReadLine();
            return line.Split('\t')[4];
        }

        private Dictionary<Tuple<long, int>, string> GetRawResourceFromAdls(List<Tuple<long, int>> resourceRefs)
        {
            var start = DateTime.UtcNow;
            var results = new Dictionary<Tuple<long, int>, string>();
            if (resourceRefs == null || resourceRefs.Count == 0)
            {
                return results;
            }

            var prevTran = -1L;
            BlobClient blobClient = null;
            foreach (var resourceRef in resourceRefs)
            {
                var transactionId = resourceRef.Item1;
                var offsetInFile = resourceRef.Item2;
                var blobName = GetBlobName(transactionId);
                if (transactionId != prevTran)
                {
                    blobClient = _adlsClient.GetBlobClient(blobName);
                }

                using var reader = new StreamReader(blobClient.OpenRead(offsetInFile));
                var line = reader.ReadLine();
                results.Add(resourceRef, line.Split('\t')[4]);
                prevTran = transactionId;
            }

            TryLogEvent("GetRawResourceFromAdls", "Warn", null, (int)(DateTime.UtcNow - start).TotalMilliseconds, $"Resources={results.Count}", start, CancellationToken.None).Wait();

            return results;
        }

        private BlobContainerClient GetAdlsContainer() // creates if does not exist
        {
            var blobServiceClient = new BlobServiceClient(_adlsConnectionString);
            var blobContainerClient = blobServiceClient.GetBlobContainerClient(_adlsContainer);

            if (!blobContainerClient.Exists())
            {
                lock (_adlsContainerLocker)
                {
                    blobContainerClient = blobServiceClient.GetBlobContainerClient(_adlsContainer);
                    if (!blobContainerClient.Exists())
                    {
                        var container = blobServiceClient.CreateBlobContainer(_adlsContainer); // TODO: This can fail on multiple VMs.
                        blobContainerClient = blobServiceClient.GetBlobContainerClient(_adlsContainer);
                    }
                }
            }

            return blobContainerClient;
        }

        public async Task<UpsertOutcome> UpsertAsync(ResourceWrapperOperation resource, CancellationToken cancellationToken)
        {
            // TODO: Remove if when Merge is min supported version
            if (_schemaInformation.Current >= SchemaVersionConstants.Merge && _mergeResourcesFeatureFlag.IsEnabled())
            {
                return (await MergeAsync(new List<ResourceWrapperOperation> { resource }, cancellationToken)).First().Value;
            }
            else
            {
                return await UpsertAsync(resource.Wrapper, resource.WeakETag, resource.AllowCreate, resource.KeepHistory, cancellationToken, resource.RequireETagOnUpdate);
            }
        }

        // TODO: Remove when Merge is min supported version
        private async Task<UpsertOutcome> UpsertAsync(
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
                using (var stream = new RecyclableMemoryStream(_memoryStreamManager, tag: nameof(SqlServerFhirDataStore)))
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

                    PopulateUpsertResourceCommand(sqlCommandWrapper, resource, keepHistory, existingVersion, stream, _coreFeatures.SupportsResourceChangeCapture);

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
        }

        // TODO: Remove when Merge is min supported version
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
            var start = DateTime.UtcNow;
            var results = new List<ResourceWrapper>();
            if (keys == null || keys.Count == 0)
            {
                return results;
            }

            using var conn = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, false);
            using var cmd = conn.CreateRetrySqlCommand();
            VLatest.GetResources.PopulateCommand(cmd, keys.Select(_ => new ResourceKeyListRow(_model.GetResourceTypeId(_.ResourceType), _.Id, _.VersionId == null ? null : int.TryParse(_.VersionId, out var version) ? version : int.MinValue))); // put min value when cannot parse so resource will be not found
            cmd.CommandTimeout = 180 + (int)(1200.0 / 10000 * keys.Count);

            using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken);
            var resources = new List<Tuple<ResourceWrapper, long?, int?>>();
            var missingResourceRefs = new List<Tuple<long, int>>();
            while (await reader.ReadAsync(cancellationToken))
            {
                var table = VLatest.Resource;
                var resourceTypeId = reader.Read(table.ResourceTypeId, 0);
                var resourceId = reader.Read(table.ResourceId, 1);
                var resourceSurrogateId = reader.Read(table.ResourceSurrogateId, 2);
                var version = reader.Read(table.Version, 3);
                var isDeleted = reader.Read(table.IsDeleted, 4);
                var isHistory = reader.Read(table.IsHistory, 5);
                var bytes = reader.GetSqlBytes(6);
                var rawResourceBytes = bytes.IsNull ? null : bytes.Value;
                var isRawResourceMetaSet = reader.Read(table.IsRawResourceMetaSet, 7);
                var searchParamHash = reader.Read(table.SearchParamHash, 8);
                var transactionId = reader.Read(table.TransactionId, 9);
                var offsetInFile = reader.Read(table.OffsetInFile, 10);

                var rawResource = string.Empty;
                if (rawResourceBytes == null)
                {
                    missingResourceRefs.Add(Tuple.Create(transactionId.Value, offsetInFile.Value));
                }
                else
                {
                    using var rawResourceStream = new MemoryStream(rawResourceBytes);
                    rawResource = _compressedRawResourceConverter.ReadCompressedRawResource(rawResourceStream);
                }

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

                resources.Add(Tuple.Create(resource, transactionId, offsetInFile));
            }

            await reader.NextResultAsync(cancellationToken);

            var missingRawResources = GetRawResourceFromAdls(missingResourceRefs);

            foreach (var resource in resources)
            {
                if (resource.Item1.RawResource.Data.Length == 0)
                {
                    resource.Item1.RawResource = new RawResource(missingRawResources[Tuple.Create(resource.Item2.Value, resource.Item3.Value)], resource.Item1.RawResource.Format, resource.Item1.RawResource.IsMetaSet);
                }
            }

            await TryLogEvent("GetAsync", "Warn", $"ReadFromAdls={missingResourceRefs.Count > 0}", (int)(DateTime.UtcNow - start).TotalMilliseconds, $"Resources={resources.Count}", start, CancellationToken.None);

            return resources.Select(_ => _.Item1).ToList();
        }

        public async Task<ResourceWrapper> GetAsync(ResourceKey key, CancellationToken cancellationToken)
        {
            // TODO: Remove if when Merge is min supported version
            if (_schemaInformation.Current >= SchemaVersionConstants.Merge && _mergeResourcesFeatureFlag.IsEnabled())
            {
                var results = await GetAsync(new List<ResourceKey> { key }, cancellationToken);
                return results.Count == 0 ? null : results[0];
            }

            // TODO: Remove all below when Merge is min supported version
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

        // TODO: Remove when Merge is min supported version
        private static string RemoveVersionIdAndLastUpdatedFromMeta(ResourceWrapper resourceWrapper)
        {
            var versionToReplace = resourceWrapper.RawResource.IsMetaSet ? resourceWrapper.Version : "1";
            var rawResource = resourceWrapper.RawResource.Data.Replace($"\"versionId\":\"{versionToReplace}\"", string.Empty, StringComparison.Ordinal);
            return rawResource.Replace($"\"lastUpdated\":\"{RemoveTrailingZerosFromMillisecondsForAGivenDate(resourceWrapper.LastModified)}\"", string.Empty, StringComparison.Ordinal);
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

        internal async Task<long> MergeResourcesBeginTransactionAsync(int resourceVersionCount, CancellationToken cancellationToken)
        {
            using var conn = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, false);
            using var cmd = conn.CreateNonRetrySqlCommand();
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = "dbo.MergeResourcesBeginTransaction";
            cmd.Parameters.AddWithValue("@Count", resourceVersionCount);
            var surrogateIdParam = new SqlParameter("@SurrogateIdRangeFirstValue", SqlDbType.BigInt) { Direction = ParameterDirection.Output };
            cmd.Parameters.Add(surrogateIdParam);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
            return (long)surrogateIdParam.Value;
        }

        internal async Task MergeResourcesCommitTransactionAsync(long surrogateIdRangeFirstValue, CancellationToken cancellationToken)
        {
            using var conn = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, false);
            using var cmd = conn.CreateNonRetrySqlCommand();
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = "dbo.MergeResourcesCommitTransaction";
            cmd.Parameters.AddWithValue("@SurrogateIdRangeFirstValue", surrogateIdRangeFirstValue);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
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

        public int GetMergeResourcesBatchSize()
        {
            using var conn = _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(CancellationToken.None, false).Result;
            using var cmd = conn.CreateRetrySqlCommand();
            cmd.CommandText = "IF object_id('dbo.Parameters') IS NOT NULL SELECT Number FROM dbo.Parameters WHERE Id = 'MergeResources.BatchSize'"; // call can be made before store is initialized
            var value = cmd.ExecuteScalarAsync(CancellationToken.None).Result;
            return value == null ? 1000 : (int)(double)value;
        }

        private class MergeResourcesFeatureFlag
        {
            private SqlConnectionWrapperFactory _sqlConnectionWrapperFactory;
            private bool _isEnabled;
            private DateTime? _lastUpdated;
            private object _databaseAccessLocker = new object();

            public MergeResourcesFeatureFlag(SqlConnectionWrapperFactory sqlConnectionWrapperFactory)
            {
                _sqlConnectionWrapperFactory = sqlConnectionWrapperFactory;
            }

            public bool IsEnabled()
            {
                if (_lastUpdated.HasValue && (DateTime.UtcNow - _lastUpdated.Value).TotalSeconds < 600)
                {
                    return _isEnabled;
                }

                lock (_databaseAccessLocker)
                {
                    _isEnabled = IsEnabledInDatabase();
                    _lastUpdated = DateTime.UtcNow;
                }

                return _isEnabled;
            }

            private bool IsEnabledInDatabase()
            {
                using var conn = _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(CancellationToken.None, false).Result;
                using var cmd = conn.CreateRetrySqlCommand();
                cmd.CommandText = "IF object_id('dbo.Parameters') IS NOT NULL SELECT Number FROM dbo.Parameters WHERE Id = @Id"; // call can be made before store is initialized
                cmd.Parameters.AddWithValue("@Id", MergeResourcesDisabledFlagId);
                var value = cmd.ExecuteScalarAsync(CancellationToken.None).Result;
                return value == null || (double)value == 0;
            }
        }
    }
}
