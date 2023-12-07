// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
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
        private readonly RecyclableMemoryStreamManager _memoryStreamManager;
        private readonly IBundleOrchestrator _bundleOrchestrator;
        private readonly CoreFeatureConfiguration _coreFeatures;
        private readonly ISqlRetryService _sqlRetryService;
        private readonly SqlStoreClient<SqlServerFhirDataStore> _sqlStoreClient;
        private readonly SqlConnectionWrapperFactory _sqlConnectionWrapperFactory;
        private readonly ICompressedRawResourceConverter _compressedRawResourceConverter;
        private readonly ILogger<SqlServerFhirDataStore> _logger;
        private readonly SchemaInformation _schemaInformation;
        private readonly IModelInfoProvider _modelInfoProvider;
        private static IgnoreInputLastUpdated _ignoreInputLastUpdated;
        private static RawResourceDeduping _rawResourceDeduping;
        private static object _flagLocker = new object();

        public SqlServerFhirDataStore(
            SqlServerFhirModel model,
            SearchParameterToSearchValueTypeMap searchParameterTypeMap,
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
            _coreFeatures = EnsureArg.IsNotNull(coreFeatures?.Value, nameof(coreFeatures));
            _bundleOrchestrator = EnsureArg.IsNotNull(bundleOrchestrator, nameof(bundleOrchestrator));
            _sqlRetryService = EnsureArg.IsNotNull(sqlRetryService, nameof(sqlRetryService));
            _sqlStoreClient = new SqlStoreClient<SqlServerFhirDataStore>(_sqlRetryService, logger);
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
                    _ignoreInputLastUpdated ??= new IgnoreInputLastUpdated(_sqlRetryService, _logger);
                }
            }

            if (_rawResourceDeduping == null)
            {
                lock (_flagLocker)
                {
                    _rawResourceDeduping ??= new RawResourceDeduping(_sqlRetryService, _logger);
                }
            }
        }

        internal SqlStoreClient<SqlServerFhirDataStore> StoreClient => _sqlStoreClient;

        internal static TimeSpan MergeResourcesTransactionHeartbeatPeriod => TimeSpan.FromSeconds(10);

        public async Task<IDictionary<DataStoreOperationIdentifier, DataStoreOperationOutcome>> MergeAsync(IReadOnlyList<ResourceWrapperOperation> resources, CancellationToken cancellationToken)
        {
            return await MergeAsync(resources, MergeOptions.Default, cancellationToken);
        }

        public async Task<IDictionary<DataStoreOperationIdentifier, DataStoreOperationOutcome>> MergeAsync(IReadOnlyList<ResourceWrapperOperation> resources, MergeOptions mergeOptions, CancellationToken cancellationToken)
        {
            var retries = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var results = await MergeInternalAsync(resources, false, false, mergeOptions.EnlistInTransaction, cancellationToken); // TODO: Pass correct retries value once we start supporting retries
                    return results;
                }
                catch (Exception e)
                {
                    var trueEx = e is AggregateException ? e.InnerException : e;
                    var sqlEx = trueEx as SqlException;
                    if (sqlEx != null && sqlEx.Number == SqlErrorCodes.Conflict && retries++ < 30) // retries on conflict should never be more than 1, so it is OK to hardcode.
                    {
                        _logger.LogWarning(e, $"Error from SQL database on {nameof(MergeAsync)} retries={{Retries}}", retries);
                        await _sqlRetryService.TryLogEvent(nameof(MergeAsync), "Warn", $"retries={retries}, error={e}, ", null, cancellationToken);
                        await Task.Delay(5000, cancellationToken);
                        continue;
                    }

                    _logger.LogError(e, $"Error from SQL database on {nameof(MergeAsync)} retries={{Retries}}", retries);
                    await _sqlRetryService.TryLogEvent(nameof(MergeAsync), "Error", $"retries={retries}, error={sqlEx}", null, cancellationToken);

                    throw trueEx;
                }
            }
        }

        // Split in a separate method to allow special logic in $import.
        internal async Task<IDictionary<DataStoreOperationIdentifier, DataStoreOperationOutcome>> MergeInternalAsync(IReadOnlyList<ResourceWrapperOperation> resources, bool keepLastUpdated, bool keepAllDeleted, bool enlistInTransaction, CancellationToken cancellationToken)
        {
            var results = new Dictionary<DataStoreOperationIdentifier, DataStoreOperationOutcome>();
            if (resources == null || resources.Count == 0)
            {
                return results;
            }

            // Ignore input resource version to get latest version from the store.
            // Include invisible records (true parameter), so version is correctly determined in case only invisible is left in store.
            var existingResources = (await GetAsync(resources.Select(r => r.Wrapper.ToResourceKey(true)).Distinct().ToList(), true, cancellationToken)).ToDictionary(r => r.ToResourceKey(true), r => r);

            // Assume that most likely case is that all resources should be updated.
            (var transactionId, var minSequenceId) = await StoreClient.MergeResourcesBeginTransactionAsync(resources.Count, cancellationToken);

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

                        _logger.LogInformation("PreconditionFailed: ResourceVersionConflict");
                        results.Add(identifier, new DataStoreOperationOutcome(new PreconditionFailedException(string.Format(Core.Resources.ResourceVersionConflict, weakETag.VersionId))));
                        continue;
                    }
                }

                // There is no previous version of this resource, check validations and then simply call SP to create new version
                if (existingResource == null)
                {
                    if (resource.IsDeleted && !keepAllDeleted)
                    {
                        // Don't bother marking the resource as deleted since it already does not exist and there are not any other resources in the batch that are not deleted
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
                            _logger.LogInformation("PreconditionFailed: IfMatchHeaderRequiredForResource");
                            results.Add(identifier, new DataStoreOperationOutcome(new PreconditionFailedException(string.Format(Core.Resources.IfMatchHeaderRequiredForResource, resource.ResourceTypeName))));
                            continue;
                        }

                        results.Add(identifier, new DataStoreOperationOutcome(new BadRequestException(string.Format(Core.Resources.IfMatchHeaderRequiredForResource, resource.ResourceTypeName))));
                        continue;
                    }

                    if (resource.IsDeleted && existingResource.IsDeleted && !keepAllDeleted)
                    {
                        // Already deleted - don't create a new version
                        results.Add(identifier, new DataStoreOperationOutcome(outcome: null));
                        continue;
                    }

                    // Check if resources are equal if its not a Delete action
                    if (!resource.IsDeleted)
                    {
                        // check if the new resource data is same as existing resource data
                        if (ExistingRawResourceIsEqualToInput(resource, existingResource, resourceExt.KeepVersion))
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
                    singleTransaction = true; // There is no way to rollback until TransactionId is added to Resource table
                }

                resource.ResourceSurrogateId = surrId;
                if (resource.Version != InitialVersion) // Do not begin transaction if all creates
                {
                    singleTransaction = true;
                }

                mergeWrappers.Add(new MergeResourceWrapper(resource, resourceExt.KeepHistory, hasVersionToCompare));
                index++;
                results.Add(identifier, new DataStoreOperationOutcome(new UpsertOutcome(resource, resource.Version == InitialVersion ? SaveOutcomeType.Created : SaveOutcomeType.Updated)));
            }

            if (mergeWrappers.Count > 0) // Do not call DB with empty input
            {
                await using (new Timer(async _ => await _sqlStoreClient.MergeResourcesPutTransactionHeartbeatAsync(transactionId, MergeResourcesTransactionHeartbeatPeriod, cancellationToken), null, TimeSpan.FromSeconds(RandomNumberGenerator.GetInt32(100) / 100.0 * MergeResourcesTransactionHeartbeatPeriod.TotalSeconds), MergeResourcesTransactionHeartbeatPeriod))
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
                                await _sqlRetryService.TryLogEvent(nameof(MergeInternalAsync), "Warn", $"retries={retries} timeoutRetries={timeoutRetries} error={e}", null, cancellationToken);
                                await Task.Delay(5000, cancellationToken);
                                continue;
                            }

                            await StoreClient.MergeResourcesCommitTransactionAsync(transactionId, e.Message, cancellationToken);
                            throw;
                        }
                    }
                }
            }
            else
            {
                await StoreClient.MergeResourcesCommitTransactionAsync(transactionId, "0 resources", cancellationToken);
            }

            return results;
        }

        internal async Task MergeResourcesWrapperAsync(long transactionId, bool singleTransaction, IReadOnlyList<MergeResourceWrapper> mergeWrappers, bool enlistInTransaction, int timeoutRetries, CancellationToken cancellationToken)
        {
            using var conn = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, enlistInTransaction);
            using var cmd = conn.CreateNonRetrySqlCommand();

            // Do not use auto generated tvp generator as it does not allow to skip compartment tvp and paramters with default values
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

        public async Task<UpsertOutcome> UpsertAsync(ResourceWrapperOperation resource, CancellationToken cancellationToken)
        {
            bool isBundleOperation = _bundleOrchestrator.IsEnabled && resource.BundleResourceContext != null;
            if (isBundleOperation)
            {
                IBundleOrchestratorOperation bundleOperation = _bundleOrchestrator.GetOperation(resource.BundleResourceContext.BundleOperationId);
                return await bundleOperation.AppendResourceAsync(resource, this, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                var mergeOutcome = await MergeAsync(new[] { resource }, cancellationToken);
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
            return await GetAsync(keys, false, cancellationToken); // do not return invisible records in public interface
        }

        private async Task<IReadOnlyList<ResourceWrapper>> GetAsync(IReadOnlyList<ResourceKey> keys, bool includeInvisible, CancellationToken cancellationToken)
        {
            return await _sqlStoreClient.GetAsync(keys, _model.GetResourceTypeId, _compressedRawResourceConverter.ReadCompressedRawResource, _model.GetResourceTypeName, cancellationToken, includeInvisible);
        }

        public async Task<ResourceWrapper> GetAsync(ResourceKey key, CancellationToken cancellationToken)
        {
            var results = await GetAsync(new[] { key }, cancellationToken);
            return results.Count == 0 ? null : results[0];
        }

        public async Task HardDeleteAsync(ResourceKey key, bool keepCurrentVersion, CancellationToken cancellationToken)
        {
            await _sqlStoreClient.HardDeleteAsync(_model.GetResourceTypeId(key.ResourceType), key.Id, keepCurrentVersion, _coreFeatures.SupportsResourceChangeCapture, cancellationToken);
        }

        public async Task BulkUpdateSearchParameterIndicesAsync(IReadOnlyCollection<ResourceWrapper> resources, CancellationToken cancellationToken)
        {
            int? failedResourceCount;
            try
            {
                // This logic relies on surrogate id in ResourceWrapper populated using database values
                var mergeWrappers = resources.Select(_ => new MergeResourceWrapper(_, false, false)).ToList();

                using var cmd = new SqlCommand("dbo.UpdateResourceSearchParams") { CommandType = CommandType.StoredProcedure, CommandTimeout = 300 + (int)(3600.0 / 10000 * mergeWrappers.Count) };
                new ResourceListTableValuedParameterDefinition("@Resources").AddParameter(cmd.Parameters, new ResourceListRowGenerator(_model, _compressedRawResourceConverter).GenerateRows(mergeWrappers));
                new ResourceWriteClaimListTableValuedParameterDefinition("@ResourceWriteClaims").AddParameter(cmd.Parameters, new ResourceWriteClaimListRowGenerator(_model, _searchParameterTypeMap).GenerateRows(mergeWrappers));
                new ReferenceSearchParamListTableValuedParameterDefinition("@ReferenceSearchParams").AddParameter(cmd.Parameters, new ReferenceSearchParamListRowGenerator(_model, _searchParameterTypeMap).GenerateRows(mergeWrappers));
                new TokenSearchParamListTableValuedParameterDefinition("@TokenSearchParams").AddParameter(cmd.Parameters, new TokenSearchParamListRowGenerator(_model, _searchParameterTypeMap).GenerateRows(mergeWrappers));
                new TokenTextListTableValuedParameterDefinition("@TokenTexts").AddParameter(cmd.Parameters, new TokenTextListRowGenerator(_model, _searchParameterTypeMap).GenerateRows(mergeWrappers));
                new StringSearchParamListTableValuedParameterDefinition("@StringSearchParams").AddParameter(cmd.Parameters, new StringSearchParamListRowGenerator(_model, _searchParameterTypeMap).GenerateRows(mergeWrappers));
                new UriSearchParamListTableValuedParameterDefinition("@UriSearchParams").AddParameter(cmd.Parameters, new UriSearchParamListRowGenerator(_model, _searchParameterTypeMap).GenerateRows(mergeWrappers));
                new NumberSearchParamListTableValuedParameterDefinition("@NumberSearchParams").AddParameter(cmd.Parameters, new NumberSearchParamListRowGenerator(_model, _searchParameterTypeMap).GenerateRows(mergeWrappers));
                new QuantitySearchParamListTableValuedParameterDefinition("@QuantitySearchParams").AddParameter(cmd.Parameters, new QuantitySearchParamListRowGenerator(_model, _searchParameterTypeMap).GenerateRows(mergeWrappers));
                new DateTimeSearchParamListTableValuedParameterDefinition("@DateTimeSearchParams").AddParameter(cmd.Parameters, new DateTimeSearchParamListRowGenerator(_model, _searchParameterTypeMap).GenerateRows(mergeWrappers));
                new ReferenceTokenCompositeSearchParamListTableValuedParameterDefinition("@ReferenceTokenCompositeSearchParams").AddParameter(cmd.Parameters, new ReferenceTokenCompositeSearchParamListRowGenerator(_model, new ReferenceSearchParamListRowGenerator(_model, _searchParameterTypeMap), new TokenSearchParamListRowGenerator(_model, _searchParameterTypeMap), _searchParameterTypeMap).GenerateRows(mergeWrappers));
                new TokenTokenCompositeSearchParamListTableValuedParameterDefinition("@TokenTokenCompositeSearchParams").AddParameter(cmd.Parameters, new TokenTokenCompositeSearchParamListRowGenerator(_model, new TokenSearchParamListRowGenerator(_model, _searchParameterTypeMap), _searchParameterTypeMap).GenerateRows(mergeWrappers));
                new TokenDateTimeCompositeSearchParamListTableValuedParameterDefinition("@TokenDateTimeCompositeSearchParams").AddParameter(cmd.Parameters, new TokenDateTimeCompositeSearchParamListRowGenerator(_model, new TokenSearchParamListRowGenerator(_model, _searchParameterTypeMap), new DateTimeSearchParamListRowGenerator(_model, _searchParameterTypeMap), _searchParameterTypeMap).GenerateRows(mergeWrappers));
                new TokenQuantityCompositeSearchParamListTableValuedParameterDefinition("@TokenQuantityCompositeSearchParams").AddParameter(cmd.Parameters, new TokenQuantityCompositeSearchParamListRowGenerator(_model, new TokenSearchParamListRowGenerator(_model, _searchParameterTypeMap), new QuantitySearchParamListRowGenerator(_model, _searchParameterTypeMap), _searchParameterTypeMap).GenerateRows(mergeWrappers));
                new TokenStringCompositeSearchParamListTableValuedParameterDefinition("@TokenStringCompositeSearchParams").AddParameter(cmd.Parameters, new TokenStringCompositeSearchParamListRowGenerator(_model, new TokenSearchParamListRowGenerator(_model, _searchParameterTypeMap), new StringSearchParamListRowGenerator(_model, _searchParameterTypeMap), _searchParameterTypeMap).GenerateRows(mergeWrappers));
                new TokenNumberNumberCompositeSearchParamListTableValuedParameterDefinition("@TokenNumberNumberCompositeSearchParams").AddParameter(cmd.Parameters, new TokenNumberNumberCompositeSearchParamListRowGenerator(_model, new TokenSearchParamListRowGenerator(_model, _searchParameterTypeMap), new NumberSearchParamListRowGenerator(_model, _searchParameterTypeMap), _searchParameterTypeMap).GenerateRows(mergeWrappers));
                var failedResourcesParam = new SqlParameter("@FailedResources", SqlDbType.Int) { Direction = ParameterDirection.Output };
                cmd.Parameters.Add(failedResourcesParam);
                await cmd.ExecuteNonQueryAsync(_sqlRetryService, _logger, cancellationToken);
                failedResourceCount = (int)failedResourcesParam.Value;
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

        private static string RemoveTrailingZerosFromMillisecondsForAGivenDate(DateTimeOffset date)
        {
            // 0000000+ -> +, 0010000+ -> 001+, 0100000+ -> 01+, 0180000+ -> 018+, 1000000 -> 1+, 1100000+ -> 11+, 1010000+ -> 101+
            // ToString("o") - Formats to 2022-03-09T01:40:52.0690000+02:00 but serialized value to string in dB is 2022-03-09T01:40:52.069+02:00
            var formattedDate = date.ToString("o", CultureInfo.InvariantCulture);
            var milliseconds = formattedDate.Substring(20, 7); // get 0690000
            var trimmedMilliseconds = milliseconds.TrimEnd('0'); // get 069
            if (milliseconds.Equals("0000000", StringComparison.Ordinal))
            {
                // When date = 2022-03-09T01:40:52.0000000+02:00, value in dB is 2022-03-09T01:40:52+02:00, we need to replace the . after second
                return formattedDate.Replace("." + milliseconds, string.Empty, StringComparison.Ordinal);
            }

            return formattedDate.Replace(milliseconds, trimmedMilliseconds, StringComparison.Ordinal);
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

        private bool ExistingRawResourceIsEqualToInput(ResourceWrapper input, ResourceWrapper existing, bool keepVersion) // call is not symmetrical, it assumes version = 1 on input.
        {
            if (!_rawResourceDeduping.IsEnabled())
            {
                return false;
            }

            var inputDate = GetJsonValue(input.RawResource.Data, "lastUpdated", false);
            var existingDate = GetJsonValue(existing.RawResource.Data, "lastUpdated", true);
            var existingVersion = GetJsonValue(existing.RawResource.Data, "versionId", true);
            if (keepVersion)
            {
                return input.RawResource.Data == existing.RawResource.Data;
            }
            else if (existingVersion == InitialVersion)
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
                // I think this should be a warning because it happens every time a resource is deleted. Maybe even info.
                _logger.LogWarning($"Cannot parse {propName} value from {(isExisting ? "existing" : "input")} {json}");
                return string.Empty;
            }

            startIndex = startIndex + propName.Length + 4;
            var endIndex = json.IndexOf("\"", startIndex, StringComparison.Ordinal);
            if (endIndex == -1)
            {
                _logger.LogWarning($"Cannot parse {propName} value from {(isExisting ? "existing" : "input")} {json}");
                return string.Empty;
            }

            var value = json.Substring(startIndex, endIndex - startIndex);

            return value;
        }

        public void Build(ICapabilityStatementBuilder builder)
        {
            EnsureArg.IsNotNull(builder, nameof(builder));

            builder.PopulateDefaultResourceInteractions()
                .SyncSearchParametersAsync()
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

        internal async Task<IReadOnlyList<ResourceWrapper>> GetResourcesByTransactionIdAsync(long transactionId, CancellationToken cancellationToken)
        {
            return await _sqlStoreClient.GetResourcesByTransactionIdAsync(transactionId, _compressedRawResourceConverter.ReadCompressedRawResource, _model.GetResourceTypeName, cancellationToken);
        }

        public async Task<ResourceWrapper> UpdateSearchParameterIndicesAsync(ResourceWrapper resource, CancellationToken cancellationToken)
        {
            await BulkUpdateSearchParameterIndicesAsync(new[] { resource }, cancellationToken);
            return resource;
        }

        public async Task<int?> GetProvisionedDataStoreCapacityAsync(CancellationToken cancellationToken = default)
        {
            return await Task.FromResult((int?)null);
        }

        private class IgnoreInputLastUpdated
        {
            private ISqlRetryService _sqlRetryService;
            private readonly ILogger<SqlServerFhirDataStore> _logger;
            private bool _isEnabled;
            private DateTime? _lastUpdated;
            private object _databaseAccessLocker = new object();

            public IgnoreInputLastUpdated(ISqlRetryService sqlRetryService, ILogger<SqlServerFhirDataStore> logger)
            {
                _sqlRetryService = sqlRetryService;
                _logger = logger;
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
                    using var cmd = new SqlCommand();
                    cmd.CommandText = "IF object_id('dbo.Parameters') IS NOT NULL SELECT Number FROM dbo.Parameters WHERE Id = 'MergeResources.IgnoreInputLastUpdated'"; // call can be made before store is initialized
                    var value = cmd.ExecuteScalarAsync(_sqlRetryService, _logger, CancellationToken.None).Result;
                    return value != null && (double)value == 1;
                }
                catch (SqlException)
                {
                    return null;
                }
            }
        }

        private class RawResourceDeduping
        {
            private ISqlRetryService _sqlRetryService;
            private readonly ILogger<SqlServerFhirDataStore> _logger;
            private bool _isEnabled;
            private DateTime? _lastUpdated;
            private object _databaseAccessLocker = new object();

            public RawResourceDeduping(ISqlRetryService sqlRetryService, ILogger<SqlServerFhirDataStore> logger)
            {
                _sqlRetryService = sqlRetryService;
                _logger = logger;
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
                    using var cmd = new SqlCommand();
                    cmd.CommandText = "IF object_id('dbo.Parameters') IS NOT NULL SELECT Number FROM dbo.Parameters WHERE Id = 'RawResourceDeduping.IsEnabled'"; // call can be made before store is initialized
                    var value = cmd.ExecuteScalarAsync(_sqlRetryService, _logger, CancellationToken.None).Result;
                    return value == null || (double)value == 1;
                }
                catch (SqlException)
                {
                    return null;
                }
            }
        }
    }
}
