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
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.FhirPath.Sprache;
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
using Microsoft.Health.Fhir.Core.Features.Persistence.Orchestration;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration;
using Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration.Merge;
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
        private const string InitialVersion = "1";

        private readonly RequestContextAccessor<IFhirRequestContext> _requestContextAccessor;
        private readonly SqlServerFhirModel _model;
        private readonly SearchParameterToSearchValueTypeMap _searchParameterTypeMap;
        private readonly VLatest.ReindexResourceTvpGenerator<IReadOnlyList<ResourceWrapper>> _reindexResourceTvpGeneratorVLatest;
        private readonly VLatest.BulkReindexResourcesTvpGenerator<IReadOnlyList<ResourceWrapper>> _bulkReindexResourcesTvpGeneratorVLatest;
        private readonly RecyclableMemoryStreamManager _memoryStreamManager;
        private readonly IBundleOrchestrator _bundleOrchestrator;
        private readonly CoreFeatureConfiguration _coreFeatures;
        private readonly ISqlRetryService _sqlRetryService;
        private readonly SqlConnectionWrapperFactory _sqlConnectionWrapperFactory;
        private readonly ICompressedRawResourceConverter _compressedRawResourceConverter;
        private readonly ILogger<SqlServerFhirDataStore> _logger;
        private readonly SchemaInformation _schemaInformation;
        private readonly IModelInfoProvider _modelInfoProvider;
        private static IgnoreInputLastUpdated _ignoreInputLastUpdated;
        private static object _flagLocker = new object();

        public SqlServerFhirDataStore(
            SqlServerFhirModel model,
            SearchParameterToSearchValueTypeMap searchParameterTypeMap,
            VLatest.ReindexResourceTvpGenerator<IReadOnlyList<ResourceWrapper>> reindexResourceTvpGeneratorVLatest,
            VLatest.BulkReindexResourcesTvpGenerator<IReadOnlyList<ResourceWrapper>> bulkReindexResourcesTvpGeneratorVLatest,
            IOptions<CoreFeatureConfiguration> coreFeatures,
            IBundleOrchestrator bundleOrchestrator,
            ISqlRetryService sqlRetryService,
            SqlConnectionWrapperFactory sqlConnectionWrapperFactory,
            ICompressedRawResourceConverter compressedRawResourceConverter,
            ILogger<SqlServerFhirDataStore> logger,
            SchemaInformation schemaInformation,
            IModelInfoProvider modelInfoProvider,
            RequestContextAccessor<IFhirRequestContext> requestContextAccessor)
        {
            _model = EnsureArg.IsNotNull(model, nameof(model));
            _searchParameterTypeMap = EnsureArg.IsNotNull(searchParameterTypeMap, nameof(searchParameterTypeMap));
            _reindexResourceTvpGeneratorVLatest = EnsureArg.IsNotNull(reindexResourceTvpGeneratorVLatest, nameof(reindexResourceTvpGeneratorVLatest));
            _bulkReindexResourcesTvpGeneratorVLatest = EnsureArg.IsNotNull(bulkReindexResourcesTvpGeneratorVLatest, nameof(bulkReindexResourcesTvpGeneratorVLatest));
            _coreFeatures = EnsureArg.IsNotNull(coreFeatures?.Value, nameof(coreFeatures));
            _bundleOrchestrator = EnsureArg.IsNotNull(bundleOrchestrator, nameof(bundleOrchestrator));
            _sqlRetryService = EnsureArg.IsNotNull(sqlRetryService, nameof(sqlRetryService));
            _sqlConnectionWrapperFactory = EnsureArg.IsNotNull(sqlConnectionWrapperFactory, nameof(sqlConnectionWrapperFactory));
            _compressedRawResourceConverter = EnsureArg.IsNotNull(compressedRawResourceConverter, nameof(compressedRawResourceConverter));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
            _schemaInformation = EnsureArg.IsNotNull(schemaInformation, nameof(schemaInformation));
            _modelInfoProvider = EnsureArg.IsNotNull(modelInfoProvider, nameof(modelInfoProvider));
            _requestContextAccessor = EnsureArg.IsNotNull(requestContextAccessor, nameof(requestContextAccessor));

            _memoryStreamManager = new RecyclableMemoryStreamManager();

            if (_ignoreInputLastUpdated == null)
            {
                lock (_flagLocker)
                {
                    _ignoreInputLastUpdated ??= new IgnoreInputLastUpdated(_sqlConnectionWrapperFactory);
                }
            }
        }

        internal static TimeSpan MergeResourcesTransactionHeartbeatPeriod => TimeSpan.FromSeconds(10);

        public async Task<IDictionary<DataStoreOperationIdentifier, DataStoreOperationOutcome>> MergeAsync(IReadOnlyList<ResourceWrapperOperation> resources, CancellationToken cancellationToken)
        {
            var retries = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    // TODO: Remove ability to enlist in transaction
                    var results = await MergeInternalAsync(resources, false, true, cancellationToken); // TODO: Pass correct retries value once we start supporting retries
                    return results;
                }
                catch (Exception e)
                {
                    var trueEx = e is AggregateException ? e.InnerException : e;
                    var sqlEx = trueEx as SqlException;
                    if (sqlEx != null && sqlEx.Number == SqlErrorCodes.Conflict && retries++ < 30) // retries on conflict should never be more than 1, so it is OK to hardcode.
                    {
                        _logger.LogWarning(e, $"Error from SQL database on {nameof(MergeAsync)} retries={{Retries}}", retries);
                        await TryLogEvent(nameof(MergeAsync), "Warn", $"retries={retries}, error={e}, ", null, cancellationToken);
                        await Task.Delay(5000, cancellationToken);
                        continue;
                    }

                    _logger.LogError(e, $"Error from SQL database on {nameof(MergeAsync)} retries={{Retries}}", retries);
                    await TryLogEvent(nameof(MergeAsync), "Error", $"retries={retries}, error={sqlEx}", null, cancellationToken);

                    throw trueEx;
                }
            }
        }

        // Split in a separate method to allow special logic in $import.
        internal async Task<IDictionary<DataStoreOperationIdentifier, DataStoreOperationOutcome>> MergeInternalAsync(IReadOnlyList<ResourceWrapperOperation> resources, bool keepLastUpdated, bool enlistInTransaction, CancellationToken cancellationToken)
        {
            var results = new Dictionary<DataStoreOperationIdentifier, DataStoreOperationOutcome>();
            if (resources == null || resources.Count == 0)
            {
                return results;
            }

            // ignore input resource version to get latest version from the store
            var existingResources = (await GetAsync(resources.Select(r => r.Wrapper.ToResourceKey(true)).Distinct().ToList(), cancellationToken)).ToDictionary(r => r.ToResourceKey(true), r => r);

            // assume that most likely case is that all resources should be updated
            (var transactionId, var minSequenceId) = await MergeResourcesBeginTransactionAsync(resources.Count, cancellationToken);

            var index = 0;
            var mergeWrappers = new List<MergeResourceWrapper>();
            var prevResourceId = string.Empty;
            var singleTransaction = enlistInTransaction;
            foreach (var resourceExt in resources) // if list contains more that one version per resource it must be sorted by id and last updated desc.
            {
                var setAsHistory = prevResourceId == resourceExt.Wrapper.ResourceId;
                prevResourceId = resourceExt.Wrapper.ResourceId;
                var weakETag = resourceExt.WeakETag;
                int? eTag = weakETag == null
                    ? null
                    : (int.TryParse(weakETag.VersionId, out var parsedETag) ? parsedETag : -1); // Set the etag to a sentinel value to enable expected failure paths when updating with both existing and nonexistent resources.

                var resource = resourceExt.Wrapper;
                var identifier = resourceExt.GetIdentifier();
                var resourceKey = resource.ToResourceKey(); // keep input version in the results to allow processing multiple versions per resource
                existingResources.TryGetValue(resource.ToResourceKey(true), out var existingResource);
                var hasVersionToCompare = false;

                // Check for any validation errors
                if (existingResource != null && eTag.HasValue && !string.Equals(eTag.ToString(), existingResource.Version, StringComparison.Ordinal))
                {
                    if (weakETag != null)
                    {
                        // The backwards compatibility behavior of Stu3 is to return 409 Conflict instead of a 412 Precondition Failed
                        if (_modelInfoProvider.Version == FhirSpecification.Stu3)
                        {
                            results.Add(identifier, new DataStoreOperationOutcome(new ResourceConflictException(weakETag)));
                            continue;
                        }

                        results.Add(identifier, new DataStoreOperationOutcome(new PreconditionFailedException(string.Format(Core.Resources.ResourceVersionConflict, weakETag.VersionId))));
                        continue;
                    }
                }

                // There is no previous version of this resource, check validations and then simply call SP to create new version
                if (existingResource == null)
                {
                    if (resource.IsDeleted)
                    {
                        // Don't bother marking the resource as deleted since it already does not exist.
                        results.Add(identifier, new DataStoreOperationOutcome(outcome: null));
                        continue;
                    }

                    if (eTag.HasValue)
                    {
                        // You can't update a resource with a specified version if the resource does not exist
                        if (weakETag != null)
                        {
                            results.Add(identifier, new DataStoreOperationOutcome(new ResourceNotFoundException(string.Format(Core.Resources.ResourceNotFoundByIdAndVersion, resource.ResourceTypeName, resource.ResourceId, weakETag.VersionId))));
                            continue;
                        }
                    }

                    if (!resourceExt.AllowCreate)
                    {
                        results.Add(identifier, new DataStoreOperationOutcome(new MethodNotAllowedException(Core.Resources.ResourceCreationNotAllowed)));
                        continue;
                    }

                    resource.Version = resourceExt.KeepVersion ? resource.Version : InitialVersion;
                    if (resource.Version == InitialVersion)
                    {
                        hasVersionToCompare = true;
                    }

                    resource.IsHistory = setAsHistory;
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
                            results.Add(identifier, new DataStoreOperationOutcome(new PreconditionFailedException(string.Format(Core.Resources.IfMatchHeaderRequiredForResource, resource.ResourceTypeName))));
                            continue;
                        }

                        results.Add(identifier, new DataStoreOperationOutcome(new BadRequestException(string.Format(Core.Resources.IfMatchHeaderRequiredForResource, resource.ResourceTypeName))));
                        continue;
                    }

                    if (resource.IsDeleted && existingResource.IsDeleted)
                    {
                        // Already deleted - don't create a new version
                        results.Add(identifier, new DataStoreOperationOutcome(outcome: null));
                        continue;
                    }

                    // check if resources are equal if its not a Delete action
                    if (!resource.IsDeleted)
                    {
                        // check if the new resource data is same as existing resource data
                        if (ExistingRawResourceIsEqualToInput(resource, existingResource))
                        {
                            // Send the existing resource in the response
                            results.Add(identifier, new DataStoreOperationOutcome(new UpsertOutcome(existingResource, SaveOutcomeType.Updated)));
                            continue;
                        }
                    }

                    var existingVersion = int.Parse(existingResource.Version);
                    var versionPlusOne = (existingVersion + 1).ToString(CultureInfo.InvariantCulture);
                    if (!resourceExt.KeepVersion) // version is set on input
                    {
                        resource.Version = versionPlusOne;
                    }

                    if (resource.Version == versionPlusOne)
                    {
                        hasVersionToCompare = true;
                    }

                    if (int.Parse(resource.Version) < existingVersion || setAsHistory) // is history
                    {
                        resource.IsHistory = true;
                    }
                }

                long surrId;
                if (!keepLastUpdated || _ignoreInputLastUpdated.IsEnabled())
                {
                    surrId = transactionId + index;
                    resource.LastModified = new DateTimeOffset(ResourceSurrogateIdHelper.ResourceSurrogateIdToLastUpdated(surrId), TimeSpan.Zero);
                    ReplaceVersionIdAndLastUpdatedInMeta(resource);
                }
                else
                {
                    var surrIdBase = ResourceSurrogateIdHelper.LastUpdatedToResourceSurrogateId(resource.LastModified.DateTime);
                    surrId = surrIdBase + minSequenceId + index;
                    ReplaceVersionIdInMeta(resource);
                    singleTransaction = true; // there is no way to rollback until TransactionId is added to Resource table
                }

                resource.ResourceSurrogateId = surrId;
                if (resource.Version != InitialVersion) // do not begin transaction if all creates
                {
                    singleTransaction = true;
                }

                mergeWrappers.Add(new MergeResourceWrapper(resource, resourceExt.KeepHistory, hasVersionToCompare));
                index++;
                results.Add(identifier, new DataStoreOperationOutcome(new UpsertOutcome(resource, resource.Version == InitialVersion ? SaveOutcomeType.Created : SaveOutcomeType.Updated)));
            }

            if (mergeWrappers.Count > 0) // do not call db with empty input
            {
                await using (new Timer(async _ => await MergeResourcesPutTransactionHeartbeatAsync(transactionId, MergeResourcesTransactionHeartbeatPeriod, cancellationToken), null, TimeSpan.FromSeconds(RandomNumberGenerator.GetInt32(100) / 100.0 * MergeResourcesTransactionHeartbeatPeriod.TotalSeconds), MergeResourcesTransactionHeartbeatPeriod))
                {
                    var retries = 0;
                    var timeoutRetries = 0;
                    while (true)
                    {
                        try
                        {
                            await MergeResourcesWrapperAsync(transactionId, singleTransaction, mergeWrappers, enlistInTransaction, timeoutRetries, cancellationToken);
                            break;
                        }
                        catch (Exception e)
                        {
                            retries++;
                            if (!enlistInTransaction && (e.IsRetriable() || (e.IsExecutionTimeout() && timeoutRetries++ < 3)))
                            {
                                _logger.LogWarning(e, $"Error on {nameof(MergeInternalAsync)} retries={{Retries}} timeoutRetries={{TimeoutRetries}}", retries, timeoutRetries);
                                await TryLogEvent(nameof(MergeInternalAsync), "Warn", $"retries={retries} timeoutRetries={timeoutRetries} error={e}", null, cancellationToken);
                                await Task.Delay(5000, cancellationToken);
                                continue;
                            }

                            await MergeResourcesCommitTransactionAsync(transactionId, e.Message, cancellationToken);
                            throw;
                        }
                    }
                }
            }
            else
            {
                await MergeResourcesCommitTransactionAsync(transactionId, "0 resources", cancellationToken);
            }

            return results;
        }

        internal async Task MergeResourcesWrapperAsync(long transactionId, bool singleTransaction, IReadOnlyList<MergeResourceWrapper> mergeWrappers, bool enlistInTransaction, int timeoutRetries, CancellationToken cancellationToken)
        {
            using var conn = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, enlistInTransaction);
            using var cmd = conn.CreateNonRetrySqlCommand();

            // do not use auto generated tvp generator as it does not allow to skip compartment tvp and paramters with default values
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = "dbo.MergeResources";
            cmd.Parameters.AddWithValue("@IsResourceChangeCaptureEnabled", _coreFeatures.SupportsResourceChangeCapture);
            cmd.Parameters.AddWithValue("@TransactionId", transactionId);
            cmd.Parameters.AddWithValue("@SingleTransaction", singleTransaction);
            new ResourceListTableValuedParameterDefinition("@Resources").AddParameter(cmd.Parameters, new ResourceListRowGenerator(_model, _compressedRawResourceConverter).GenerateRows(mergeWrappers));
            new ResourceWriteClaimListTableValuedParameterDefinition("@ResourceWriteClaims").AddParameter(cmd.Parameters, new ResourceWriteClaimListRowGenerator(_model, _searchParameterTypeMap).GenerateRows(mergeWrappers));
            new ReferenceSearchParamListTableValuedParameterDefinition("@ReferenceSearchParams").AddParameter(cmd.Parameters, new ReferenceSearchParamListRowGenerator(_model, _searchParameterTypeMap).GenerateRows(mergeWrappers));
            new TokenSearchParamListTableValuedParameterDefinition("@TokenSearchParams").AddParameter(cmd.Parameters, new TokenSearchParamListRowGenerator(_model, _searchParameterTypeMap).GenerateRows(mergeWrappers));
            new TokenTextListTableValuedParameterDefinition("@TokenTexts").AddParameter(cmd.Parameters, new TokenTextListRowGenerator(_model, _searchParameterTypeMap).GenerateRows(mergeWrappers));
            new StringSearchParamListTableValuedParameterDefinition("@StringSearchParams").AddParameter(cmd.Parameters, new StringSearchParamListRowGenerator(_model, _searchParameterTypeMap).GenerateRows(mergeWrappers));
            new UriSearchParamListTableValuedParameterDefinition("@UriSearchParams").AddParameter(cmd.Parameters, new UriSearchParamListRowGenerator(_model, _searchParameterTypeMap).GenerateRows(mergeWrappers));
            new NumberSearchParamListTableValuedParameterDefinition("@NumberSearchParams").AddParameter(cmd.Parameters, new NumberSearchParamListRowGenerator(_model, _searchParameterTypeMap).GenerateRows(mergeWrappers));
            new QuantitySearchParamListTableValuedParameterDefinition("@QuantitySearchParams").AddParameter(cmd.Parameters, new QuantitySearchParamListRowGenerator(_model, _searchParameterTypeMap).GenerateRows(mergeWrappers));
            new DateTimeSearchParamListTableValuedParameterDefinition("@DateTimeSearchParms").AddParameter(cmd.Parameters, new DateTimeSearchParamListRowGenerator(_model, _searchParameterTypeMap).GenerateRows(mergeWrappers));
            new ReferenceTokenCompositeSearchParamListTableValuedParameterDefinition("@ReferenceTokenCompositeSearchParams").AddParameter(cmd.Parameters, new ReferenceTokenCompositeSearchParamListRowGenerator(_model, new ReferenceSearchParamListRowGenerator(_model, _searchParameterTypeMap), new TokenSearchParamListRowGenerator(_model, _searchParameterTypeMap), _searchParameterTypeMap).GenerateRows(mergeWrappers));
            new TokenTokenCompositeSearchParamListTableValuedParameterDefinition("@TokenTokenCompositeSearchParams").AddParameter(cmd.Parameters, new TokenTokenCompositeSearchParamListRowGenerator(_model, new TokenSearchParamListRowGenerator(_model, _searchParameterTypeMap), _searchParameterTypeMap).GenerateRows(mergeWrappers));
            new TokenDateTimeCompositeSearchParamListTableValuedParameterDefinition("@TokenDateTimeCompositeSearchParams").AddParameter(cmd.Parameters, new TokenDateTimeCompositeSearchParamListRowGenerator(_model, new TokenSearchParamListRowGenerator(_model, _searchParameterTypeMap), new DateTimeSearchParamListRowGenerator(_model, _searchParameterTypeMap), _searchParameterTypeMap).GenerateRows(mergeWrappers));
            new TokenQuantityCompositeSearchParamListTableValuedParameterDefinition("@TokenQuantityCompositeSearchParams").AddParameter(cmd.Parameters, new TokenQuantityCompositeSearchParamListRowGenerator(_model, new TokenSearchParamListRowGenerator(_model, _searchParameterTypeMap), new QuantitySearchParamListRowGenerator(_model, _searchParameterTypeMap), _searchParameterTypeMap).GenerateRows(mergeWrappers));
            new TokenStringCompositeSearchParamListTableValuedParameterDefinition("@TokenStringCompositeSearchParams").AddParameter(cmd.Parameters, new TokenStringCompositeSearchParamListRowGenerator(_model, new TokenSearchParamListRowGenerator(_model, _searchParameterTypeMap), new StringSearchParamListRowGenerator(_model, _searchParameterTypeMap), _searchParameterTypeMap).GenerateRows(mergeWrappers));
            new TokenNumberNumberCompositeSearchParamListTableValuedParameterDefinition("@TokenNumberNumberCompositeSearchParams").AddParameter(cmd.Parameters, new TokenNumberNumberCompositeSearchParamListRowGenerator(_model, new TokenSearchParamListRowGenerator(_model, _searchParameterTypeMap), new NumberSearchParamListRowGenerator(_model, _searchParameterTypeMap), _searchParameterTypeMap).GenerateRows(mergeWrappers));
            cmd.CommandTimeout = 300 + (int)(3600.0 / 10000 * (timeoutRetries + 1) * mergeWrappers.Count);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        private async Task MergeResourcesPutTransactionHeartbeatAsync(long transactionId, TimeSpan heartbeatPeriod, CancellationToken cancellationToken)
        {
            try
            {
                using var cmd = new SqlCommand() { CommandText = "dbo.MergeResourcesPutTransactionHeartbeat", CommandType = CommandType.StoredProcedure, CommandTimeout = (heartbeatPeriod.Seconds / 3) + 1 }; // +1 to avoid = SQL default timeout value
                cmd.Parameters.AddWithValue("@TransactionId", transactionId);
                await _sqlRetryService.ExecuteSql(cmd, async (sql, cancellationToken) => await sql.ExecuteNonQueryAsync(cancellationToken), _logger, null, cancellationToken);
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, $"Error from SQL database on {nameof(MergeResourcesPutTransactionHeartbeatAsync)}");
            }
        }

        internal async Task TryLogEvent(string process, string status, string text, DateTime? startDate, CancellationToken cancellationToken)
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

        public async Task<UpsertOutcome> UpsertAsync(ResourceWrapperOperation resource, CancellationToken cancellationToken)
        {
            bool isBundleOperation = _bundleOrchestrator.IsEnabled && resource.BundleOperationId != null;
            if (isBundleOperation)
            {
                IBundleOrchestratorOperation bundleOperation = _bundleOrchestrator.GetOperation(resource.BundleOperationId.Value);
                return await bundleOperation.AppendResourceAsync(resource, this, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                var mergeOutcome = await MergeAsync(new List<ResourceWrapperOperation> { resource }, cancellationToken);
                DataStoreOperationOutcome dataStoreOperationOutcome = mergeOutcome.First().Value;

                if (dataStoreOperationOutcome.IsOperationSuccessful)
                {
                    return dataStoreOperationOutcome.UpsertOutcome;
                }
                else
                {
                    throw dataStoreOperationOutcome.Exception;
                }
            }
        }

        public async Task<IReadOnlyList<ResourceWrapper>> GetAsync(IReadOnlyList<ResourceKey> keys, CancellationToken cancellationToken)
        {
            if (keys == null || keys.Count == 0)
            {
                return new List<ResourceWrapper>();
            }

            using var cmd = new SqlCommand() { CommandText = "dbo.GetResources", CommandType = CommandType.StoredProcedure, CommandTimeout = 180 + (int)(2400.0 / 10000 * keys.Count) };
            var tvpRows = keys.Select(_ => new ResourceKeyListRow(_model.GetResourceTypeId(_.ResourceType), _.Id, _.VersionId == null ? null : int.TryParse(_.VersionId, out var version) ? version : int.MinValue));
            new ResourceKeyListTableValuedParameterDefinition("@ResourceKeys").AddParameter(cmd.Parameters, tvpRows);
            var start = DateTime.UtcNow;
            var timeoutRetries = 0;
            while (true)
            {
                try
                {
                    return await _sqlRetryService.ExecuteSqlDataReader(cmd, (reader) => { return ReadResourceWrapper(reader, false); }, _logger, null, cancellationToken);
                }
                catch (Exception e)
                {
                    if (e.IsExecutionTimeout() && timeoutRetries++ < 3)
                    {
                        _logger.LogWarning(e, $"Error on {nameof(GetAsync)} timeoutRetries={{TimeoutRetries}}", timeoutRetries);
                        await TryLogEvent(nameof(GetAsync), "Warn", $"timeout retries={timeoutRetries}", start, cancellationToken);
                        await Task.Delay(5000, cancellationToken);
                        continue;
                    }
                }
            }
        }

        private ResourceWrapper ReadResourceWrapper(SqlDataReader reader, bool readRequestMethod)
        {
            var resourceTypeId = reader.Read(VLatest.Resource.ResourceTypeId, 0);
            var resourceId = reader.Read(VLatest.Resource.ResourceId, 1);
            var resourceSurrogateId = reader.Read(VLatest.Resource.ResourceSurrogateId, 2);
            var version = reader.Read(VLatest.Resource.Version, 3);
            var isDeleted = reader.Read(VLatest.Resource.IsDeleted, 4);
            var isHistory = reader.Read(VLatest.Resource.IsHistory, 5);
            var rawResourceBytes = reader.GetSqlBytes(6).Value;
            var isRawResourceMetaSet = reader.Read(VLatest.Resource.IsRawResourceMetaSet, 7);
            var searchParamHash = reader.Read(VLatest.Resource.SearchParamHash, 8);
            var requestMethod = readRequestMethod ? reader.Read(VLatest.Resource.RequestMethod, 9) : null;

            using var rawResourceStream = new MemoryStream(rawResourceBytes);
            var rawResource = _compressedRawResourceConverter.ReadCompressedRawResource(rawResourceStream);

            return new ResourceWrapper(
                resourceId,
                version.ToString(CultureInfo.InvariantCulture),
                _model.GetResourceTypeName(resourceTypeId),
                new RawResource(rawResource, FhirResourceFormat.Json, isMetaSet: isRawResourceMetaSet),
                readRequestMethod ? new ResourceRequest(requestMethod) : null,
                new DateTimeOffset(ResourceSurrogateIdHelper.ResourceSurrogateIdToLastUpdated(resourceSurrogateId), TimeSpan.Zero),
                isDeleted,
                searchIndices: null,
                compartmentIndices: null,
                lastModifiedClaims: null,
                searchParameterHash: searchParamHash,
                resourceSurrogateId: resourceSurrogateId)
            {
                IsHistory = isHistory,
            };
        }

        private ResourceDateKey ReadResourceDateKeyWrapper(SqlDataReader reader)
        {
            var resourceTypeId = reader.Read(VLatest.Resource.ResourceTypeId, 0);
            var resourceId = reader.Read(VLatest.Resource.ResourceId, 1);
            var resourceSurrogateId = reader.Read(VLatest.Resource.ResourceSurrogateId, 2);
            var version = reader.Read(VLatest.Resource.Version, 3);
            var isDeleted = reader.Read(VLatest.Resource.IsDeleted, 4);

            return new ResourceDateKey(
                _model.GetResourceTypeName(resourceTypeId),
                resourceId,
                resourceSurrogateId,
                version.ToString(CultureInfo.InvariantCulture),
                isDeleted);
        }

        public async Task<IReadOnlyList<ResourceDateKey>> GetResourceVervionsAsync(IReadOnlyList<ResourceDateKey> keys, CancellationToken cancellationToken)
        {
            var resources = new List<ResourceDateKey>();
            if (keys == null || keys.Count == 0)
            {
                return resources;
            }

            using var cmd = new SqlCommand() { CommandText = "dbo.GetResourceVersions", CommandType = CommandType.StoredProcedure, CommandTimeout = 180 + (int)(1200.0 / 10000 * keys.Count) };
            var tvpRows = keys.Select(_ => new ResourceDateKeyListRow(_model.GetResourceTypeId(_.ResourceType), _.Id, _.ResourceSurrogateId));
            new ResourceDateKeyListTableValuedParameterDefinition("@ResourceDateKeys").AddParameter(cmd.Parameters, tvpRows);
            var table = VLatest.Resource;
            resources = await _sqlRetryService.ExecuteSqlDataReader(
                cmd,
                (reader) =>
                {
                    var resourceTypeId = reader.Read(table.ResourceTypeId, 0);
                    var resourceId = reader.Read(table.ResourceId, 1);
                    var resourceSurrogateId = reader.Read(table.ResourceSurrogateId, 2);
                    var version = reader.Read(table.Version, 3);
                    return new ResourceDateKey(_model.GetResourceTypeName(resourceTypeId), resourceId, resourceSurrogateId, version.ToString(CultureInfo.InvariantCulture));
                },
                _logger,
                null,
                cancellationToken);
            return resources;
        }

        public async Task<ResourceWrapper> GetAsync(ResourceKey key, CancellationToken cancellationToken)
        {
            var results = await GetAsync(new List<ResourceKey> { key }, cancellationToken);
            return results.Count == 0 ? null : results[0];
        }

        public async Task HardDeleteAsync(ResourceKey key, bool keepCurrentVersion, CancellationToken cancellationToken)
        {
            using var sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true);
            using var sqlCommandWrapper = sqlConnectionWrapper.CreateRetrySqlCommand();
            VLatest.HardDeleteResource.PopulateCommand(sqlCommandWrapper, _model.GetResourceTypeId(key.ResourceType), key.Id, keepCurrentVersion, _coreFeatures.SupportsResourceChangeCapture);
            await sqlCommandWrapper.ExecuteNonQueryAsync(cancellationToken);
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

        private static string RemoveVersionIdAndLastUpdatedFromMeta(ResourceWrapper resourceWrapper)
        {
            var versionToReplace = resourceWrapper.RawResource.IsMetaSet ? resourceWrapper.Version : "1";
            var rawResource = resourceWrapper.RawResource.Data.Replace($"\"versionId\":\"{versionToReplace}\"", string.Empty, StringComparison.Ordinal);
            return rawResource.Replace($"\"lastUpdated\":\"{RemoveTrailingZerosFromMillisecondsForAGivenDate(resourceWrapper.LastModified)}\"", string.Empty, StringComparison.Ordinal);
        }

        private void ReplaceVersionIdInMeta(ResourceWrapper resourceWrapper)
        {
            if (resourceWrapper.Version == InitialVersion) // version is already correct
            {
                return;
            }

            var version = GetJsonValue(resourceWrapper.RawResource.Data, "versionId", false);
            var rawResourceData = resourceWrapper.RawResource.Data.Replace($"\"versionId\":\"{version}\"", $"\"versionId\":\"{resourceWrapper.Version}\"", StringComparison.Ordinal);
            resourceWrapper.RawResource = new RawResource(rawResourceData, FhirResourceFormat.Json, true);
        }

        private void ReplaceVersionIdAndLastUpdatedInMeta(ResourceWrapper resourceWrapper)
        {
            var date = GetJsonValue(resourceWrapper.RawResource.Data, "lastUpdated", false);
            string rawResourceData;
            if (resourceWrapper.Version == InitialVersion) // version is already correct
            {
                rawResourceData = resourceWrapper.RawResource.Data
                                    .Replace($"\"lastUpdated\":\"{date}\"", $"\"lastUpdated\":\"{RemoveTrailingZerosFromMillisecondsForAGivenDate(resourceWrapper.LastModified)}\"", StringComparison.Ordinal);
            }
            else
            {
                var version = GetJsonValue(resourceWrapper.RawResource.Data, "versionId", false);
                rawResourceData = resourceWrapper.RawResource.Data
                                    .Replace($"\"versionId\":\"{version}\"", $"\"versionId\":\"{resourceWrapper.Version}\"", StringComparison.Ordinal)
                                    .Replace($"\"lastUpdated\":\"{date}\"", $"\"lastUpdated\":\"{RemoveTrailingZerosFromMillisecondsForAGivenDate(resourceWrapper.LastModified)}\"", StringComparison.Ordinal);
            }

            resourceWrapper.RawResource = new RawResource(rawResourceData, FhirResourceFormat.Json, true);
        }

        private bool ExistingRawResourceIsEqualToInput(ResourceWrapper input, ResourceWrapper existing) // call is not symmetrical, it assumes version = 1 on input.
        {
            var inputDate = GetJsonValue(input.RawResource.Data, "lastUpdated", false);
            var existingDate = GetJsonValue(existing.RawResource.Data, "lastUpdated", true);
            var existingVersion = GetJsonValue(existing.RawResource.Data, "versionId", true);
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
        private string GetJsonValue(string json, string propName, bool isExisting)
        {
            var startIndex = json.IndexOf($"\"{propName}\":\"", StringComparison.Ordinal);
            if (startIndex == -1)
            {
                _logger.LogError($"Cannot parse {propName} value from {(isExisting ? "existing" : "input")} {json}");
                return string.Empty;
            }

            startIndex = startIndex + propName.Length + 4;
            var endIndex = json.IndexOf("\"", startIndex, StringComparison.Ordinal);
            if (endIndex == -1)
            {
                _logger.LogError($"Cannot parse {propName} value from {(isExisting ? "existing" : "input")} {json}");
                return string.Empty;
            }

            var value = json.Substring(startIndex, endIndex - startIndex);

            return value;
        }

        public void Build(ICapabilityStatementBuilder builder)
        {
            EnsureArg.IsNotNull(builder, nameof(builder));

            var task = Task.Run(() => builder.PopulateDefaultResourceInteractions()
                .SyncSearchParametersAsync(CancellationToken.None));
            task.Wait();
            builder.AddGlobalSearchParameters()
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

        internal async Task<long> MergeResourcesGetTransactionVisibilityAsync(CancellationToken cancellationToken)
        {
            using var cmd = new SqlCommand() { CommandText = "dbo.MergeResourcesGetTransactionVisibility", CommandType = CommandType.StoredProcedure };
            var transactionIdParam = new SqlParameter("@TransactionId", SqlDbType.BigInt) { Direction = ParameterDirection.Output };
            cmd.Parameters.Add(transactionIdParam);
            await _sqlRetryService.ExecuteSql(cmd, async (sql, cancellationToken) => await sql.ExecuteNonQueryAsync(cancellationToken), _logger, null, cancellationToken);
            return (long)transactionIdParam.Value;
        }

        internal async Task<(long TransactionId, int Sequence)> MergeResourcesBeginTransactionAsync(int resourceVersionCount, CancellationToken cancellationToken, DateTime? heartbeatDate = null)
        {
            using var cmd = new SqlCommand() { CommandText = "dbo.MergeResourcesBeginTransaction", CommandType = CommandType.StoredProcedure };
            cmd.Parameters.AddWithValue("@Count", resourceVersionCount);
            var transactionIdParam = new SqlParameter("@TransactionId", SqlDbType.BigInt) { Direction = ParameterDirection.Output };
            cmd.Parameters.Add(transactionIdParam);
            var sequenceParam = new SqlParameter("@SequenceRangeFirstValue", SqlDbType.Int) { Direction = ParameterDirection.Output };
            cmd.Parameters.Add(sequenceParam);
            if (heartbeatDate.HasValue)
            {
                cmd.Parameters.AddWithValue("@HeartbeatDate", heartbeatDate.Value);
            }

            await _sqlRetryService.ExecuteSql(cmd, async (sql, cancellationToken) => await sql.ExecuteNonQueryAsync(cancellationToken), _logger, null, cancellationToken);
            return ((long)transactionIdParam.Value, (int)sequenceParam.Value);
        }

        internal async Task<int> MergeResourcesDeleteInvisibleHistory(long transactionId, CancellationToken cancellationToken)
        {
            using var cmd = new SqlCommand() { CommandText = "dbo.MergeResourcesDeleteInvisibleHistory", CommandType = CommandType.StoredProcedure };
            cmd.Parameters.AddWithValue("@TransactionId", transactionId);
            var affectedRowsParam = new SqlParameter("@affectedRows", SqlDbType.Int) { Direction = ParameterDirection.Output };
            cmd.Parameters.Add(affectedRowsParam);
            await _sqlRetryService.ExecuteSql(cmd, async (sql, cancellationToken) => await sql.ExecuteNonQueryAsync(cancellationToken), _logger, null, cancellationToken);
            return (int)affectedRowsParam.Value;
        }

        internal async Task MergeResourcesCommitTransactionAsync(long transactionId, string failureReason, CancellationToken cancellationToken)
        {
            using var cmd = new SqlCommand() { CommandText = "dbo.MergeResourcesCommitTransaction", CommandType = CommandType.StoredProcedure };
            cmd.Parameters.AddWithValue("@TransactionId", transactionId);
            if (failureReason != null)
            {
                cmd.Parameters.AddWithValue("@FailureReason", failureReason);
            }

            await _sqlRetryService.ExecuteSql(cmd, async (sql, cancellationToken) => await sql.ExecuteNonQueryAsync(cancellationToken), _logger, null, cancellationToken);
        }

        internal async Task MergeResourcesPutTransactionInvisibleHistoryAsync(long transactionId, CancellationToken cancellationToken)
        {
            using var cmd = new SqlCommand() { CommandText = "dbo.MergeResourcesPutTransactionInvisibleHistory", CommandType = CommandType.StoredProcedure };
            cmd.Parameters.AddWithValue("@TransactionId", transactionId);
            await _sqlRetryService.ExecuteSql(cmd, async (sql, cancellationToken) => await sql.ExecuteNonQueryAsync(cancellationToken), _logger, null, cancellationToken);
        }

        internal async Task<int> MergeResourcesAdvanceTransactionVisibilityAsync(CancellationToken cancellationToken)
        {
            using var cmd = new SqlCommand() { CommandText = "dbo.MergeResourcesAdvanceTransactionVisibility", CommandType = CommandType.StoredProcedure };
            var affectedRowsParam = new SqlParameter("@AffectedRows", SqlDbType.Int) { Direction = ParameterDirection.Output };
            cmd.Parameters.Add(affectedRowsParam);
            await _sqlRetryService.ExecuteSql(cmd, async (sql, cancellationToken) => await sql.ExecuteNonQueryAsync(cancellationToken), _logger, null, cancellationToken);
            var affectedRows = (int)affectedRowsParam.Value;
            return affectedRows;
        }

        internal async Task<IReadOnlyList<long>> MergeResourcesGetTimeoutTransactionsAsync(int timeoutSec, CancellationToken cancellationToken)
        {
            using var cmd = new SqlCommand() { CommandText = "dbo.MergeResourcesGetTimeoutTransactions", CommandType = CommandType.StoredProcedure };
            cmd.Parameters.AddWithValue("@TimeoutSec", timeoutSec);
            return await _sqlRetryService.ExecuteSqlDataReader(cmd, (reader) => { return reader.GetInt64(0); }, _logger, null, cancellationToken);
        }

        internal async Task<IReadOnlyList<(long TransactionId, DateTime? VisibleDate, DateTime? InvisibleHistoryRemovedDate)>> GetTransactionsAsync(long startNotInclusiveTranId, long endInclusiveTranId, CancellationToken cancellationToken, DateTime? endDate = null)
        {
            using var cmd = new SqlCommand() { CommandText = "dbo.GetTransactions", CommandType = CommandType.StoredProcedure };
            cmd.Parameters.AddWithValue("@StartNotInclusiveTranId", startNotInclusiveTranId);
            cmd.Parameters.AddWithValue("@EndInclusiveTranId", endInclusiveTranId);
            if (endDate.HasValue)
            {
                cmd.Parameters.AddWithValue("@EndDate", endDate.Value);
            }

            return await _sqlRetryService.ExecuteSqlDataReader(
                cmd,
                (reader) =>
                {
                    return (reader.Read(VLatest.Transactions.SurrogateIdRangeFirstValue, 0),
                            reader.Read(VLatest.Transactions.VisibleDate, 1),
                            reader.Read(VLatest.Transactions.InvisibleHistoryRemovedDate, 2));
                },
                _logger,
                null,
                cancellationToken);
        }

        internal async Task<IReadOnlyList<ResourceWrapper>> GetResourcesByTransactionIdAsync(long transactionId, CancellationToken cancellationToken)
        {
            using var cmd = new SqlCommand() { CommandText = "dbo.GetResourcesByTransactionId", CommandType = CommandType.StoredProcedure, CommandTimeout = 600 };
            cmd.Parameters.AddWithValue("@TransactionId", transactionId);
            return await _sqlRetryService.ExecuteSqlDataReader(cmd, (reader) => { return ReadResourceWrapper(reader, true); }, _logger, null, cancellationToken);
        }

        internal async Task<IReadOnlyList<ResourceDateKey>> GetResourceDateKeysByTransactionIdAsync(long transactionId, CancellationToken cancellationToken)
        {
            using var cmd = new SqlCommand() { CommandText = "dbo.GetResourcesByTransactionId", CommandType = CommandType.StoredProcedure, CommandTimeout = 600 };
            cmd.Parameters.AddWithValue("@TransactionId", transactionId);
            cmd.Parameters.AddWithValue("@IncludeHistory", true);
            cmd.Parameters.AddWithValue("@ReturnResourceKeysOnly", true);
            return await _sqlRetryService.ExecuteSqlDataReader(cmd, ReadResourceDateKeyWrapper, _logger, null, cancellationToken);
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

        private class IgnoreInputLastUpdated
        {
            private SqlConnectionWrapperFactory _sqlConnectionWrapperFactory;
            private bool _isEnabled;
            private DateTime? _lastUpdated;
            private object _databaseAccessLocker = new object();

            public IgnoreInputLastUpdated(SqlConnectionWrapperFactory sqlConnectionWrapperFactory)
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
                    if (_lastUpdated.HasValue && (DateTime.UtcNow - _lastUpdated.Value).TotalSeconds < 600)
                    {
                        return _isEnabled;
                    }

                    var isEnabled = IsEnabledInDatabase();
                    if (isEnabled.HasValue)
                    {
                        _isEnabled = isEnabled.Value;
                        _lastUpdated = DateTime.UtcNow;
                    }
                }

                return _isEnabled;
            }

            private bool? IsEnabledInDatabase()
            {
                try
                {
                    using var conn = _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(CancellationToken.None, false).Result;
                    using var cmd = conn.CreateRetrySqlCommand();
                    cmd.CommandText = "IF object_id('dbo.Parameters') IS NOT NULL SELECT Number FROM dbo.Parameters WHERE Id = 'MergeResources.IgnoreInputLastUpdated'"; // call can be made before store is initialized
                    var value = cmd.ExecuteScalarAsync(CancellationToken.None).Result;
                    return value != null && (double)value == 1;
                }
                catch (SqlException)
                {
                    return null;
                }
            }
        }
    }
}
