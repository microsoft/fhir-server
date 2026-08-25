// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
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
using Microsoft.Health.Abstractions.Features.Transactions;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Import;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Persistence.Orchestration;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration;
using Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration.Merge;
using Microsoft.Health.Fhir.ValueSets;
using Microsoft.Health.SqlServer.Features.Client;
using Microsoft.Health.SqlServer.Features.Schema;
using Microsoft.Health.SqlServer.Features.Storage;
using Microsoft.IO;
using Microsoft.SqlServer.Management.XEvent;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    /// <summary>
    /// A SQL Server-backed <see cref="IFhirDataStore"/>.
    /// </summary>
    internal class SqlServerFhirDataStore : IFhirDataStore, IProvideCapability
    {
        private const string InitialVersion = "1";
        internal const string MergeApplicationName = "MergeResources";

        private readonly RequestContextAccessor<IFhirRequestContext> _requestContextAccessor;
        private readonly SqlServerFhirModel _model;
        private readonly SearchParameterToSearchValueTypeMap _searchParameterTypeMap;
        private readonly RecyclableMemoryStreamManager _memoryStreamManager;
        private readonly IBundleOrchestrator _bundleOrchestrator;
        private readonly CoreFeatureConfiguration _coreFeatures;
        private readonly ISqlRetryService _sqlRetryService;
        private readonly SqlStoreClient _sqlStoreClient;
        private readonly SqlConnectionWrapperFactory _sqlConnectionWrapperFactory;
        private readonly SqlTransactionHandler _sqlTransactionHandler;
        private readonly ICompressedRawResourceConverter _compressedRawResourceConverter;
        private readonly ILogger<SqlServerFhirDataStore> _logger;
        private readonly SchemaInformation _schemaInformation;
        private readonly IModelInfoProvider _modelInfoProvider;
        private readonly IImportErrorSerializer _importErrorSerializer;
        private static CachedParameter<SqlServerFhirDataStore> _ignoreInputLastUpdated;
        private static CachedParameter<SqlServerFhirDataStore> _ignoreInputVersion;
        private static CachedParameter<SqlServerFhirDataStore> _rawResourceDeduping;
        private static readonly object _flagLocker = new object();

        public SqlServerFhirDataStore(
            SqlServerFhirModel model,
            SearchParameterToSearchValueTypeMap searchParameterTypeMap,
            IOptions<CoreFeatureConfiguration> coreFeatures,
            IBundleOrchestrator bundleOrchestrator,
            ISqlRetryService sqlRetryService,
            SqlConnectionWrapperFactory sqlConnectionWrapperFactory,
            SqlTransactionHandler sqlTransactionHandler,
            ICompressedRawResourceConverter compressedRawResourceConverter,
            ILogger<SqlServerFhirDataStore> logger,
            SchemaInformation schemaInformation,
            IModelInfoProvider modelInfoProvider,
            RequestContextAccessor<IFhirRequestContext> requestContextAccessor,
            IImportErrorSerializer importErrorSerializer,
            SqlStoreClient storeClient)
        {
            _model = EnsureArg.IsNotNull(model, nameof(model));
            _searchParameterTypeMap = EnsureArg.IsNotNull(searchParameterTypeMap, nameof(searchParameterTypeMap));
            _coreFeatures = EnsureArg.IsNotNull(coreFeatures?.Value, nameof(coreFeatures));
            _bundleOrchestrator = EnsureArg.IsNotNull(bundleOrchestrator, nameof(bundleOrchestrator));
            _sqlRetryService = EnsureArg.IsNotNull(sqlRetryService, nameof(sqlRetryService));
            _sqlStoreClient = EnsureArg.IsNotNull(storeClient, nameof(storeClient));
            _sqlConnectionWrapperFactory = EnsureArg.IsNotNull(sqlConnectionWrapperFactory, nameof(sqlConnectionWrapperFactory));
            _sqlTransactionHandler = EnsureArg.IsNotNull(sqlTransactionHandler, nameof(sqlTransactionHandler));
            _compressedRawResourceConverter = EnsureArg.IsNotNull(compressedRawResourceConverter, nameof(compressedRawResourceConverter));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
            _schemaInformation = EnsureArg.IsNotNull(schemaInformation, nameof(schemaInformation));
            _modelInfoProvider = EnsureArg.IsNotNull(modelInfoProvider, nameof(modelInfoProvider));
            _requestContextAccessor = EnsureArg.IsNotNull(requestContextAccessor, nameof(requestContextAccessor));
            _importErrorSerializer = EnsureArg.IsNotNull(importErrorSerializer, nameof(importErrorSerializer));

            _memoryStreamManager = new RecyclableMemoryStreamManager();

            if (_ignoreInputLastUpdated == null)
            {
                lock (_flagLocker)
                {
                    _ignoreInputLastUpdated ??= new CachedParameter<SqlServerFhirDataStore>("MergeResources.IgnoreInputLastUpdated.IsEnabled", 0, _logger);
                }
            }

            if (_ignoreInputVersion == null)
            {
                lock (_flagLocker)
                {
                    _ignoreInputVersion ??= new CachedParameter<SqlServerFhirDataStore>("MergeResources.IgnoreInputVersion.IsEnabled", 0, _logger);
                }
            }

            if (_rawResourceDeduping == null)
            {
                lock (_flagLocker)
                {
                    _rawResourceDeduping ??= new CachedParameter<SqlServerFhirDataStore>("MergeResources.RawResourceDeduping.IsEnabled", 1, _logger);
                }
            }
        }

        internal SqlStoreClient StoreClient => _sqlStoreClient;

        internal static TimeSpan MergeResourcesTransactionHeartbeatPeriod => TimeSpan.FromSeconds(10);

        public async Task TryLogEvent(string process, string status, string text, DateTime? startDate, CancellationToken cancellationToken)
        {
            await _sqlRetryService.TryLogEvent(process, status, text, startDate, cancellationToken);
        }

        public async Task<MergeOutcome> MergeAsync(IReadOnlyList<ResourceWrapperOperation> resources, CancellationToken cancellationToken)
        {
            return await MergeAsync(resources, MergeOptions.Default, cancellationToken);
        }

        public async Task<MergeOutcome> MergeAsync(IReadOnlyList<ResourceWrapperOperation> resources, MergeOptions mergeOptions, CancellationToken cancellationToken)
        {
            const int maxRetries = 30;
            const int defaultRetryDelayInMilliseconds = 1000;

            var retries = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Capture whether this attempt is enlisted in an ambient C# transaction (e.g. a sequential transaction bundle).
                bool wasEnlistedInAmbientTransaction = mergeOptions.EnlistInTransaction && _sqlTransactionHandler.SqlTransactionScope != null;

                try
                {
                    var results = await MergeInternalAsync(
                        resources,
                        keepLastUpdated: false,
                        keepAllDeleted: false,
                        mergeOptions.EnlistInTransaction,
                        retries == 0,
                        eventualConsistency: false,
                        isBundleTransaction: mergeOptions.IsBundleTransaction,
                        cancellationToken); // TODO: Pass correct retries value once we start supporting retries
                    return results;
                }
                catch (Exception e)
                {
                    var trueEx = e is AggregateException ? e.InnerException : e;
                    var sqlEx = trueEx as SqlException;
                    if (sqlEx != null)
                    {
                        // SQL Conflict (50409) - It indicates a conflict with another concurrent operation, which could be resolved by retrying.
                        // SQL Duplicated Key Conflict (50424) - Rare scenario. It indicates an issue with the surrogate ID generation. Regular API calls should not retry.

                        if (sqlEx.Number == SqlErrorCodes.Conflict)
                        {
                            // A merge batch can contain several operations, and dbo.MergeResources raises one generic
                            // conflict for the whole batch without identifying the row that caused it. Picking the
                            // first guarded operation would report 412 for a resource whose precondition still holds
                            // whenever an unrelated operation in the same batch is what actually collided, so the
                            // conflict is correlated to the operation whose comparison no longer matches the database.
                            ResourceWrapperOperation resourceWithVersionPrecondition = await FindOperationWithViolatedVersionPreconditionAsync(resources, cancellationToken);
                            string expectedVersion = resourceWithVersionPrecondition?.WeakETag?.VersionId ?? resourceWithVersionPrecondition?.ComparedVersion;

                            if (wasEnlistedInAmbientTransaction)
                            {
                                if (resourceWithVersionPrecondition != null)
                                {
                                    ThrowResourceVersionConflict(expectedVersion);
                                }

                                // The ambient SqlTransaction is now zombied by this conflict; retrying within it is futile. Fail fast with a 409 so the client can retry the whole transaction bundle.
                                _logger.LogWarning(e, "Conflict: ResourceConcurrentUpdateConflict in ambient transaction; failing fast (SQL error {SqlErrorNumber}).", sqlEx.Number);
                                throw new ResourceConflictException(Resources.ResourceConcurrentUpdateConflict);
                            }

                            if (resourceWithVersionPrecondition?.ComparedVersion != null)
                            {
                                ThrowResourceVersionConflict(expectedVersion);
                            }

                            if (retries++ >= maxRetries)
                            {
                                _logger.LogInformation("PreconditionFailed: ResourceConcurrentUpdateConflict");
                                throw new PreconditionFailedException(Resources.ResourceConcurrentUpdateConflict);
                            }
                            else
                            {
                                _logger.LogWarning(e, $"Error from SQL database on {nameof(MergeAsync)} retries={{Retries}} (Conflict)", retries);
                                await _sqlRetryService.TryLogEvent(nameof(MergeAsync), "Warn", $"retries={retries}, error={e}, ", null, cancellationToken);

                                await Task.Delay(defaultRetryDelayInMilliseconds, cancellationToken);
                                continue;
                            }
                        }
                        else if (sqlEx.Number == FhirSqlErrorCodes.SurrogateIdCollision && retries++ < maxRetries)
                        {
                            _logger.LogWarning(e, $"Error from SQL database on {nameof(MergeAsync)} retries={{Retries}} (SurrogateIdCollision)", retries);
                            await _sqlRetryService.TryLogEvent(nameof(MergeAsync), "Warn", $"retries={retries}, error={e}, ", null, cancellationToken);

                            await Task.Delay(defaultRetryDelayInMilliseconds, cancellationToken);
                            continue;
                        }
                        else if (sqlEx.IsSearchParameterConcurrencyConflict())
                        {
                            _logger.LogWarning(sqlEx, "Optimistic concurrency conflict occurred while calling dbo.MergeResourcesAndSearchParams");
                            throw new BadRequestException(Core.Resources.SearchParameterConcurrencyConflict);
                        }
                        else if (sqlEx.IsReindexJobConflict())
                        {
                            _logger.LogWarning(sqlEx, $"Error calling dbo.MergeResourcesAndSearchParams. {sqlEx.Message}");
                            throw new JobConflictException(sqlEx.Message);
                        }
                    }

                    _logger.LogError(e, $"Error from SQL database on {nameof(MergeAsync)} retries={{Retries}}", retries);
                    await _sqlRetryService.TryLogEvent(nameof(MergeAsync), "Error", $"retries={retries}, error={trueEx}", null, cancellationToken);

                    throw trueEx;
                }
            }
        }

        private async Task<MergeOutcome> MergeInternalAsync(IReadOnlyList<ResourceWrapperOperation> resources, bool keepLastUpdated, bool keepAllDeleted, bool enlistInTransaction, bool useReplicasForReads, bool eventualConsistency, bool isBundleTransaction, CancellationToken cancellationToken)
        {
            var results = new Dictionary<DataStoreOperationIdentifier, DataStoreOperationOutcome>();
            if (resources == null || resources.Count == 0)
            {
                return MergeOutcome.Empty;
            }

            var singleTransaction = enlistInTransaction || !eventualConsistency;

            // Ignore input resource version to get latest version from the store.
            // Include invisible records (true parameter), so version is correctly determined in case only invisible is left in store.
            //// A read-only replica lags the primary by an unbounded amount, and every version comparison below is
            //// made against this snapshot - including the ones that reject an operation outright. A batch carrying
            //// any caller supplied precondition is therefore always read from the primary, so a client whose
            //// If-Match value is in fact current is never rejected on the strength of a stale replica read.
            bool hasVersionPreconditions = resources.Any(resource => resource.WeakETag != null || resource.ComparedVersion != null);
            var existingResources = (await GetAsync(resources.Select(r => r.Wrapper.ToResourceKey(true)).Distinct().ToList(), true, useReplicasForReads && !hasVersionPreconditions, cancellationToken)).ToDictionary(r => r.ToResourceKey(true), r => r);

            // Assume that most likely case is that all resources should be updated.
            // TODO: MergeResourcesBeginTransaction accepts new parameter allowing to throw exception on overload.
            // TODO: Set this parameter to true when 429 instead of intenal waits is desired. Make sure that exception is NOT thrown only for API calls.
            (var transactionId, var minSequenceId) = await StoreClient.MergeResourcesBeginTransactionAsync(resources.Count, cancellationToken);

            var index = 0;
            var mergeWrappersWithVersions = new List<(MergeResourceWrapper Wrapper, bool KeepVersion, int ResourceVersion, int? ExistingVersion)>();
            ResourceKey prevResourceKey = null;
            foreach (var resourceExt in resources) // if list contains more that one version per resource it must be sorted by id and last updated DESC.
            {
                var metaHistory = true;
                var resource = resourceExt.Wrapper;
                var setAsHistory = prevResourceKey == resource.ToResourceKey(true); // this assumes that first resource version is the latest one
                //// negative versions are historical by definition
                if (resourceExt.KeepVersion && int.Parse(resource.Version) < 0)
                {
                    setAsHistory = true;
                }

                prevResourceKey = resource.ToResourceKey(true);
                var weakETag = resourceExt.WeakETag;

                // Set the etag to a sentinel value (via ParseWeakETagVersionOrSentinel) to enable expected failure
                // paths when updating with both existing and nonexistent resources.
                int? eTag = weakETag == null
                    ? null
                    : ParseWeakETagVersionOrSentinel(weakETag.VersionId);

                existingResources.TryGetValue(resource.ToResourceKey(true), out var existingResource);
                var hasVersionToCompare = false;
                var existingVersion = 0;

                // Check for any validation errors
                if (weakETag != null &&
                    existingResource != null &&
                    !string.Equals(eTag.ToString(), existingResource.Version, StringComparison.Ordinal))
                {
                    _logger.LogInformation("PreconditionFailed: ResourceVersionConflict");
                    results.Add(resourceExt.GetIdentifier(), new DataStoreOperationOutcome(new PreconditionFailedException(string.Format(Core.Resources.ResourceVersionConflict, weakETag.VersionId))));
                    continue;
                }

                // There is no previous version of this resource, check validations and then simply call SP to create new version
                if (existingResource == null)
                {
                    if (resourceExt.ComparedVersion != null)
                    {
                        _logger.LogInformation("PreconditionFailed: ResourceVersionConflict");
                        results.Add(resourceExt.GetIdentifier(), new DataStoreOperationOutcome(new PreconditionFailedException(string.Format(Core.Resources.ResourceVersionConflict, resourceExt.ComparedVersion))));
                        continue;
                    }

                    if (resource.IsDeleted && !keepAllDeleted)
                    {
                        if (weakETag != null)
                        {
                            // A caller supplied If-Match can never be satisfied by a target that does not exist at all, so this
                            // guarded delete must fail its precondition rather than silently succeed as an idempotent no-op.
                            // This matches Cosmos DB's behavior for the same disappearance. An unguarded delete (no WeakETag) of
                            // an already missing target remains the pre-existing idempotent no-op below.
                            _logger.LogInformation("PreconditionFailed: ResourceVersionConflict");
                            results.Add(resourceExt.GetIdentifier(), new DataStoreOperationOutcome(new PreconditionFailedException(string.Format(Core.Resources.ResourceVersionConflict, weakETag.VersionId))));
                            continue;
                        }

                        // Don't bother marking the resource as deleted since it already does not exist and there are not any other resources in the batch that are not deleted
                        results.Add(resourceExt.GetIdentifier(), new DataStoreOperationOutcome(outcome: null));
                        continue;
                    }

                    if (eTag.HasValue)
                    {
                        // You can't update a resource with a specified version if the resource does not exist
                        if (weakETag != null)
                        {
                            results.Add(resourceExt.GetIdentifier(), new DataStoreOperationOutcome(new ResourceNotFoundException(string.Format(Core.Resources.ResourceNotFoundByIdAndVersion, resource.ResourceTypeName, resource.ResourceId, weakETag.VersionId))));
                            continue;
                        }
                    }

                    if (!resourceExt.AllowCreate)
                    {
                        results.Add(resourceExt.GetIdentifier(), new DataStoreOperationOutcome(new MethodNotAllowedException(Core.Resources.ResourceCreationNotAllowed)));
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
                    if (resourceExt.RequireETagOnUpdate && !eTag.HasValue && !(resource.IsDeleted && existingResource.IsDeleted))
                    {
                        // This is a versioned update and no version was specified
                        // TODO: Add this to SQL error codes in AB#88286
                        // The backwards compatibility behavior of Stu3 is to return 412 Precondition Failed instead of a 400 Bad Request
                        if (_modelInfoProvider.Version == FhirSpecification.Stu3)
                        {
                            _logger.LogInformation("PreconditionFailed: IfMatchHeaderRequiredForResource");
                            results.Add(resourceExt.GetIdentifier(), new DataStoreOperationOutcome(new PreconditionFailedException(string.Format(Core.Resources.IfMatchHeaderRequiredForResource, resource.ResourceTypeName))));
                            continue;
                        }

                        _logger.LogInformation("BadRequest: IfMatchHeaderRequiredForResource");
                        results.Add(resourceExt.GetIdentifier(), new DataStoreOperationOutcome(new BadRequestException(string.Format(Core.Resources.IfMatchHeaderRequiredForResource, resource.ResourceTypeName))));
                        continue;
                    }

                    if (resourceExt.ComparedVersion != null &&
                        !string.Equals(resourceExt.ComparedVersion, existingResource.Version, StringComparison.Ordinal))
                    {
                        _logger.LogInformation("PreconditionFailed: ResourceVersionConflict");
                        results.Add(resourceExt.GetIdentifier(), new DataStoreOperationOutcome(new PreconditionFailedException(string.Format(Core.Resources.ResourceVersionConflict, resourceExt.ComparedVersion))));
                        continue;
                    }

                    if (resource.IsDeleted && existingResource.IsDeleted && !keepAllDeleted)
                    {
                        // Deleting an already deleted resource is a no-op, so nothing is sent to dbo.MergeResources and
                        // the atomic version comparison that stored procedure performs never runs for this operation.
                        // The batch snapshot above cannot stand in for it: it is an unlocked, point-in-time read
                        // taken several round trips earlier, so it neither blocks a concurrent writer of this
                        // resource nor observes one that is already in flight.
                        PreconditionFailedException noOpDeletePrecondition = await VerifyNoOpVersionPreconditionAsync(resourceExt, enlistInTransaction, cancellationToken);
                        if (noOpDeletePrecondition != null)
                        {
                            results.Add(resourceExt.GetIdentifier(), new DataStoreOperationOutcome(noOpDeletePrecondition));
                            continue;
                        }

                        // Already deleted - don't create a new version
                        results.Add(resourceExt.GetIdentifier(), new DataStoreOperationOutcome(outcome: null));
                        continue;
                    }

                    // Check if resources are equal if its not a Delete action
                    if (!resource.IsDeleted)
                    {
                        // check if the new resource data is same as existing resource data
                        if (ExistingRawResourceIsEqualToInput(resource.RawResource, existingResource.RawResource, resourceExt.KeepVersion))
                        {
                            // As with the already-deleted branch above, an update that changes nothing never reaches
                            // dbo.MergeResources, so a guarded operation must have its precondition confirmed against
                            // an authoritative, lock-serialized read before this success is returned.
                            PreconditionFailedException noOpUpdatePrecondition = await VerifyNoOpVersionPreconditionAsync(resourceExt, enlistInTransaction, cancellationToken);
                            if (noOpUpdatePrecondition != null)
                            {
                                results.Add(resourceExt.GetIdentifier(), new DataStoreOperationOutcome(noOpUpdatePrecondition));
                                continue;
                            }

                            _logger.LogInformation("Update operation resulted in no changes for resource {ResourceType}/{ResourceId}.", resource.ResourceTypeName, resource.ResourceId);

                            // Send the existing resource in the response
                            results.Add(resourceExt.GetIdentifier(), new DataStoreOperationOutcome(new UpsertOutcome(existingResource, SaveOutcomeType.Updated)));
                            continue;
                        }
                        else if (!resourceExt.MetaHistory && ChangesAreOnlyInMetadata(resource, existingResource))
                        {
                            _logger.LogInformation("Update operation modified only meta fields for resource {ResourceType}/{ResourceId}.", resource.ResourceTypeName, resource.ResourceId);
                            metaHistory = false;
                        }
                    }

                    existingVersion = int.Parse(existingResource.Version);
                    var versionPlusOne = (existingVersion + 1).ToString(CultureInfo.InvariantCulture);
                    if (!resourceExt.KeepVersion) // version is set on input
                    {
                        resource.Version = versionPlusOne;
                    }

                    // This is not part of the above check to cover the case of importing data in version order.
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
                if (!keepLastUpdated || _ignoreInputLastUpdated.IsEnabled(_sqlRetryService))
                {
                    surrId = transactionId + index;
                    resource.LastModified = surrId.ToLastUpdated();
                    SyncVersionIdAndLastUpdatedInMeta(resource);
                }
                else
                {
                    var surrIdBase = resource.LastModified.ToSurrogateId();
                    surrId = surrIdBase + minSequenceId + index;
                    SyncVersionIdInMeta(resource);
                    singleTransaction = true; // There is no way to rollback until TransactionId is added to Resource table
                }

                resource.ResourceSurrogateId = surrId;
                if (resource.Version != InitialVersion) // Do not begin transaction if all creates
                {
                    singleTransaction = true;
                }

                // exclude resources for search param deletes, as they are handled by reindex
                if (resourceExt.PendingSearchParameterStatus == null
                    || (resourceExt.PendingSearchParameterStatus.Status != SearchParameterStatus.PendingDelete
                        && resourceExt.PendingSearchParameterStatus.Status != SearchParameterStatus.PendingHardDelete))
                {
                    mergeWrappersWithVersions.Add((new MergeResourceWrapper(resource, resourceExt.KeepHistory && metaHistory, hasVersionToCompare), resourceExt.KeepVersion, int.Parse(resource.Version), existingVersion));
                }

                index++;
                results.Add(resourceExt.GetIdentifier(), new DataStoreOperationOutcome(new UpsertOutcome(resource, resource.Version == InitialVersion ? SaveOutcomeType.Created : SaveOutcomeType.Updated)));
            }

            // In case the operation is atomic (i.e., bundle transaction) and there are validation errors, then nothing should be persisted at the database.
            // Instead, the errors should be reported and ensure the operation is atomic.
            if (isBundleTransaction && results.Where(r => !r.Value.IsOperationSuccessful).Any())
            {
                return new MergeOutcome(MergeOutcomeFinalState.CompletedWithFailures, results);
            }

            // Resources with input versions (keepVersion=true) might not have hasVersionToCompare set. Fix it here.
            // Resources with keepVersion=true must be in separate call, and not mixed with keepVersion=false ones.
            // Sort them in groups by resource id and order by version.
            // In each group find the smallest version higher then existing
            prevResourceKey = null;
            var notSetInResoureGroup = false;
            foreach (var mergeWrapper in mergeWrappersWithVersions.Where(x => x.KeepVersion && x.ExistingVersion != 0).OrderBy(x => x.Wrapper.ResourceWrapper.ToResourceKey(true)).ThenBy(x => x.ResourceVersion))
            {
                if (prevResourceKey != mergeWrapper.Wrapper.ResourceWrapper.ToResourceKey(true)) // this should reset flag on each resource id group including first.
                {
                    notSetInResoureGroup = true;
                }

                prevResourceKey = mergeWrapper.Wrapper.ResourceWrapper.ToResourceKey(true);

                if (notSetInResoureGroup && mergeWrapper.ResourceVersion > mergeWrapper.ExistingVersion)
                {
                    mergeWrapper.Wrapper.HasVersionToCompare = true;
                    notSetInResoureGroup = false;
                }
            }

            var pendingStatuses = resources.Where(_ => _.PendingSearchParameterStatus != null).Select(_ => _.PendingSearchParameterStatus).ToList();
            if (mergeWrappersWithVersions.Count > 0 || pendingStatuses.Count > 0) // Do not call DB with empty input
            {
                await using (new Timer(async _ => await _sqlStoreClient.MergeResourcesPutTransactionHeartbeatAsync(transactionId, MergeResourcesTransactionHeartbeatPeriod, cancellationToken), null, TimeSpan.FromSeconds(RandomNumberGenerator.GetInt32(100) / 100.0 * MergeResourcesTransactionHeartbeatPeriod.TotalSeconds), MergeResourcesTransactionHeartbeatPeriod))
                {
                    var retries = 0;
                    var timeoutRetries = 0;
                    while (true)
                    {
                        try
                        {
                            await MergeResourcesWrapperAsync(transactionId, singleTransaction, mergeWrappersWithVersions.Select(_ => _.Wrapper).ToList(), enlistInTransaction, timeoutRetries, pendingStatuses, cancellationToken);
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

                            if (singleTransaction) // if not single SQL transaction, then let TransactionWatchdog to try rolling forward
                            {
                                await StoreClient.MergeResourcesCommitTransactionAsync(transactionId, e.Message, cancellationToken);
                            }

                            throw;
                        }
                    }
                }
            }
            else
            {
                await StoreClient.MergeResourcesCommitTransactionAsync(transactionId, "0 resources", cancellationToken);
            }

            // If this is not an atomic operations, even if there are unsuccessful results, the operation state will be set as 'Completed'.
            // For atomic operations, reaching this level means that all results are successful.
            return new MergeOutcome(MergeOutcomeFinalState.Completed, results);
        }

        internal async Task<IReadOnlyList<string>> ImportResourcesAsync(IReadOnlyList<ImportResource> resources, ImportMode importMode, bool allowNegativeVersions, bool eventualConsistency, CancellationToken cancellationToken)
        {
            if (resources.Count == 0) // do not go to the database
            {
                return new List<string>();
            }

            (List<ImportResource> Loaded, List<ImportResource> Conflicts) results;
            var maxRetries = GetMaxRetries(resources, importMode);
            var retries = 0;
            while (true)
            {
                try
                {
                    results = await ImportResourcesInternalAsync(retries == 0);
                    break;
                }
                catch (Exception e)
                {
                    var sqlEx = (e is SqlException ? e : e.InnerException) as SqlException;
                    if (sqlEx != null && sqlEx.Number == FhirSqlErrorCodes.MergeResourcesConcurrentCallsIsAboveOptimal)
                    {
                        var delayMs = RandomNumberGenerator.GetInt32(1000, 5000);
                        _logger.LogWarning(e, $"Throttling detected on {nameof(ImportResourcesInternalAsync)}, backing off for {{DelayMs}}ms resources={{Resources}}", delayMs, resources.Count);
                        await Task.Delay(delayMs, cancellationToken);
                        continue;
                    }

                    if (sqlEx != null && (sqlEx.Number == SqlErrorCodes.Conflict || sqlEx.Number == FhirSqlErrorCodes.SurrogateIdCollision) && retries++ < maxRetries)
                    {
                        _logger.LogWarning(e, $"Error on {nameof(ImportResourcesInternalAsync)} retries={{Retries}} resources={{Resources}} error={{SqlErrorCode}}", retries, resources.Count, sqlEx.Number);
                        await Task.Delay(retries > 3 ? 10 : 1000, cancellationToken); // if >3 assume that it is id generation problem
                        continue;
                    }

                    _logger.LogError(e, $"Error on {nameof(ImportResourcesInternalAsync)} retries={{Retries}} resources={{Resources}}", retries, resources.Count);
                    await StoreClient.TryLogEvent(nameof(ImportResourcesInternalAsync), "Error", $"retries={retries} resources={resources.Count} error={e}", null, cancellationToken);

                    throw;
                }
            }

            var dups = resources.Except(results.Loaded).Except(results.Conflicts)?.ToList();

            return GetErrors(dups, results.Conflicts);

            int GetMaxRetries(IReadOnlyList<ImportResource> resources, ImportMode importMode)
            {
                return importMode == ImportMode.IncrementalLoad && resources.Any(_ => _.KeepLastUpdated) ? 80000 / resources.Count : 30; // 80K is id sequence rollover
            }

            List<string> GetErrors(IReadOnlyCollection<ImportResource> dups, IReadOnlyCollection<ImportResource> conflicts)
            {
                var errors = new List<string>();
                foreach (var resource in dups)
                {
                    errors.Add(_importErrorSerializer.Serialize(resource.Index, string.Format(Resources.FailedToImportDuplicate, resource.ResourceWrapper.ResourceId, resource.Index), resource.Offset));
                }

                foreach (var resource in conflicts)
                {
                    errors.Add(_importErrorSerializer.Serialize(resource.Index, string.Format(Resources.FailedToImportConflictingVersion, resource.ResourceWrapper.ResourceId, resource.Index), resource.Offset));
                }

                return errors;
            }

            async Task<(List<ImportResource> Loaded, List<ImportResource> Conflicts)> ImportResourcesInternalAsync(bool useReplicasForReads)
            {
                var loaded = new List<ImportResource>();
                var conflicts = new List<ImportResource>();
                if (importMode == ImportMode.InitialLoad)
                {
                    var inputsDedupped = resources.GroupBy(_ => _.ResourceWrapper.ToResourceKey(true)).Select(_ => _.OrderBy(_ => _.ResourceWrapper.LastModified).First()).ToList();
                    var current = new HashSet<ResourceKey>((await GetAsync(inputsDedupped.Select(_ => _.ResourceWrapper.ToResourceKey(true)).ToList(), cancellationToken)).Select(_ => _.ToResourceKey(true)));
                    loaded.AddRange(inputsDedupped.Where(i => !current.TryGetValue(i.ResourceWrapper.ToResourceKey(true), out _)));
                    await Merge(loaded, false, useReplicasForReads);
                }
                else if (importMode == ImportMode.IncrementalLoad)
                {
                    if (_ignoreInputVersion.IsEnabled(_sqlRetryService))
                    {
                        foreach (var resource in resources)
                        {
                            resource.KeepVersion = false;
                            ReplaceVersionId(resource.ResourceWrapper, InitialVersion);
                        }
                    }

                    // Dedup by last updated - take first version for single last updated, prefer large version.
                    // for records without explicit last updated dedup on resource id only.
                    // Note: Surrogate id on ResourceWrapper remains 0 at this point.
                    var inputsDedupped = resources
                        .GroupBy(_ => new ResourceDateKey(
                                             _model.GetResourceTypeId(_.ResourceWrapper.ResourceTypeName),
                                             _.ResourceWrapper.ResourceId,
                                             _.KeepLastUpdated ? _.ResourceWrapper.LastModified.ToSurrogateId() : 0,
                                             null))
                        .Select(_ => _.OrderByDescending(_ => _.ResourceWrapper.Version).First())
                        .ToList();

                    // Dedup on lastUpdated against database
                    var matchedOnLastUpdated =
                        (await StoreClient.GetResourceVersionsAsync(inputsDedupped.Where(_ => _.KeepLastUpdated).Select(_ => _.ResourceWrapper.ToResourceDateKey(_model.GetResourceTypeId, true)).ToList(), _compressedRawResourceConverter.ReadCompressedRawResource, cancellationToken))
                            .Where(_ => _.Key.VersionId == "0")
                            .ToDictionary(_ => new ResourceDateKey(_.Key.ResourceTypeId, _.Key.Id, _.Key.ResourceSurrogateId, null), _ => _);
                    var fullyDedupped = new List<ImportResource>();
                    foreach (var input in inputsDedupped)
                    {
                        if (matchedOnLastUpdated.TryGetValue(input.ResourceWrapper.ToResourceDateKey(_model.GetResourceTypeId, ignoreVersion: true), out var existing))
                        {
                            if (((input.KeepVersion && input.ResourceWrapper.Version == existing.Matched.Version) || !input.KeepVersion)
                                && ExistingRawResourceIsEqualToInput(input.ResourceWrapper.RawResource, existing.Matched.RawResource, false))
                            {
                                loaded.Add(input);
                            }
                            else
                            {
                                conflicts.Add(input);
                            }
                        }
                        else
                        {
                            fullyDedupped.Add(input);
                        }
                    }

                    // make sure that data with explicit and default last updated are merged separately
                    await MergeVersioned(fullyDedupped.Where(_ => _.KeepVersion).ToList(), useReplicasForReads); // if keep version is true, keep last updated is true too.

                    await MergeUnversioned(fullyDedupped.Where(_ => _.KeepLastUpdated && !_.KeepVersion).ToList(), true, useReplicasForReads);

                    await MergeUnversioned(fullyDedupped.Where(_ => !_.KeepLastUpdated && !_.KeepVersion).ToList(), false, useReplicasForReads);
                }

                return (loaded, conflicts);

                List<ImportResource> RemoveVersionOutOfSyncWithLastUpdatedConflicts(IEnumerable<ImportResource> inputs)
                {
                    // Remove conflicts where versions and last updated are out of order
                    ResourceKey prevResourceKey = null;
                    var prevVersion = int.MaxValue;
                    var inputsWithVersion = new List<ImportResource>();
                    foreach (var input in inputs.OrderBy(_ => _.ResourceWrapper.ToResourceKey(true)).ThenByDescending(_ => _.ResourceWrapper.LastModified))
                    {
                        if (prevResourceKey != input.ResourceWrapper.ToResourceKey(true))
                        {
                            prevVersion = int.MaxValue;
                        }

                        var inputVersion = int.Parse(input.ResourceWrapper.Version);
                        if (inputVersion < 0) // negatives shoud not participate in this logic
                        {
                            if (allowNegativeVersions)
                            {
                                inputsWithVersion.Add(input);
                            }
                            else
                            {
                                conflicts.Add(input);
                            }

                            continue;
                        }

                        if (inputVersion >= prevVersion)
                        {
                            conflicts.Add(input);
                        }
                        else
                        {
                            inputsWithVersion.Add(input);
                        }

                        prevResourceKey = input.ResourceWrapper.ToResourceKey(true);
                        prevVersion = inputVersion;
                    }

                    return inputsWithVersion;
                }

                async Task MergeVersioned(List<ImportResource> inputs, bool useReplicasForReads)
                {
                    // Dedup by version via ToResourceKey - prefer latest dates.
                    var inputsWithVersionTemp = inputs.GroupBy(_ => _.ResourceWrapper.ToResourceKey()).Select(_ => _.OrderByDescending(_ => _.ResourceWrapper.LastModified).First());

                    var inputsWithVersion = RemoveVersionOutOfSyncWithLastUpdatedConflicts(inputsWithVersionTemp);

                    // Search the db for versions that match the import resources with version so we can filter duplicates from the import.
                    var versionsInDb = (await GetAsync(inputsWithVersion.Select(_ => _.ResourceWrapper.ToResourceKey()).ToList(), cancellationToken)).ToDictionary(_ => _.ToResourceKey(), _ => _);

                    // If resources are identical consider already loaded. We should compare both last updated and raw resource
                    // if dates or raw resource do not match consider as conflict
                    var loadCandidates = new List<ImportResource>();
                    foreach (var input in inputsWithVersion)
                    {
                        if (versionsInDb.TryGetValue(input.ResourceWrapper.ToResourceKey(), out var versionInDb))
                        {
                            conflicts.Add(input); // Ckecks on lastUpdated were already run above.
                        }
                        else
                        {
                            loadCandidates.Add(input);
                        }
                    }

                    // check whether input last updated and version are in sync with the database. skip for negatives.
                    var loadCandidatesWithIntVersion = loadCandidates.Select(_ => new { Resource = _, IntVersion = int.Parse(_.ResourceWrapper.Version) }).ToList();
                    var toBeLoaded = loadCandidatesWithIntVersion.Where(_ => _.IntVersion < 0).ToList();
                    var currentInDb = (await GetAsync(loadCandidatesWithIntVersion.Where(_ => _.IntVersion > 0).Select(_ => _.Resource.ResourceWrapper.ToResourceKey(true)).Distinct().ToList(), cancellationToken)).ToDictionary(_ => _.ToResourceKey(true), _ => new { Resource = _, IntVersion = int.Parse(_.Version) });
                    foreach (var input in loadCandidatesWithIntVersion.Where(_ => _.IntVersion > 0))
                    {
                        if (currentInDb.TryGetValue(input.Resource.ResourceWrapper.ToResourceKey(true), out var inDb)
                            && ((inDb.Resource.LastModified > input.Resource.ResourceWrapper.LastModified && inDb.IntVersion < input.IntVersion)
                                || (inDb.Resource.LastModified < input.Resource.ResourceWrapper.LastModified && inDb.IntVersion > input.IntVersion)))
                        {
                            conflicts.Add(input.Resource); // version and last updated are not aligned
                        }
                        else
                        {
                            toBeLoaded.Add(input);
                        }
                    }

                    // Import resource versions that don't exist in the db.
                    // Sorting is used in merge to set isHistory - don't change it without updating that method!
                    // negative versions should be last
                    await Merge(toBeLoaded.OrderBy(_ => _.Resource.ResourceWrapper.ResourceId).ThenBy(_ => _.IntVersion < 0).ThenByDescending(_ => _.Resource.ResourceWrapper.LastModified).Select(_ => _.Resource), true, useReplicasForReads);
                    loaded.AddRange(toBeLoaded.Select(_ => _.Resource));
                }

                async Task MergeUnversioned(List<ImportResource> inputs, bool keepLastUpdated, bool useReplicasForReads)
                {
                    // Check curent version in the database.
                    var currentInDb = (await GetAsync(inputs.Select(_ => _.ResourceWrapper.ToResourceKey(true)).Distinct().ToList(), cancellationToken)).ToDictionary(_ => _.ToResourceKey(true), _ => _);

                    // If last updated on input resource is below current, then need to check the "fit".
                    var inputsNoVersionForCheck = new List<ImportResource>();
                    foreach (var input in inputs)
                    {
                        // Include inputs only with explicit lastUpdated (input.KeepLastUpdated = true)
                        if (currentInDb.TryGetValue(input.ResourceWrapper.ToResourceKey(true), out var current) && input.KeepLastUpdated && input.ResourceWrapper.LastModified < current.LastModified)
                        {
                            inputsNoVersionForCheck.Add(input);
                        }
                    }

                    // Ensure that the imported resources can "fit" in the db. We want to keep versionId alinged to lastUpdated and sequential if possible.
                    // Note: surrogate id is populated from last updated by ToResourceDateKey(), therefore we can trust this value as part of dictionary key.
                    var versionSlots = (await StoreClient.GetResourceVersionsAsync(inputsNoVersionForCheck.Select(_ => _.ResourceWrapper.ToResourceDateKey(_model.GetResourceTypeId, true)).ToList(), _compressedRawResourceConverter.ReadCompressedRawResource, cancellationToken)).ToDictionary(_ => new ResourceDateKey(_.Key.ResourceTypeId, _.Key.Id, _.Key.ResourceSurrogateId, null), _ => _);
                    foreach (var input in inputsNoVersionForCheck.OrderBy(_ => _.ResourceWrapper.ToResourceKey(true)).ThenByDescending(_ => _.ResourceWrapper.LastModified))
                    {
                        var resourceDateKey = input.ResourceWrapper.ToResourceDateKey(_model.GetResourceTypeId, true);
                        versionSlots.TryGetValue(resourceDateKey, out var existing);
                        input.KeepVersion = true;
                        var versionIdInt = int.Parse(existing.Key.VersionId);
                        if (versionIdInt == 0) // though this check was done above, racing conditions can stil lead to extra matches
                        {
                            if (ExistingRawResourceIsEqualToInput(input.ResourceWrapper.RawResource, existing.Matched.RawResource, false))
                            {
                                loaded.Add(input);
                            }
                            else
                            {
                                conflicts.Add(input);
                            }
                        }
                        else if (versionIdInt > 0)
                        {
                            input.ResourceWrapper.Version = existing.Key.VersionId;
                        }
                        else
                        {
                            if (allowNegativeVersions)
                            {
                                input.ResourceWrapper.Version = existing.Key.VersionId;
                            }
                            else
                            {
                                conflicts.Add(input); // no version slot available and negative versions are not allowed
                            }
                        }
                    }

                    var inputNoConflict = inputs.Except(conflicts).Except(loaded);

                    // Make sure that version is incremented taking into account current state in the database.
                    ResourceKey prevResourceKey = null;
                    var version = 0;
                    foreach (var input in inputNoConflict.Where(_ => _.KeepLastUpdated && !_.KeepVersion).OrderBy(_ => _.ResourceWrapper.ToResourceKey(true)).ThenBy(_ => _.ResourceWrapper.LastModified))
                    {
                        if (prevResourceKey != input.ResourceWrapper.ToResourceKey(true))
                        {
                            version = currentInDb.TryGetValue(input.ResourceWrapper.ToResourceKey(true), out var current) ? int.Parse(current.Version) : 0;
                        }

                        input.ResourceWrapper.Version = (++version).ToString();
                        input.KeepVersion = true;
                        prevResourceKey = input.ResourceWrapper.ToResourceKey(true);
                    }

                    // Finally merge the resources to the db.
                    await Merge(inputNoConflict.OrderBy(_ => _.ResourceWrapper.ToResourceKey(true)).ThenByDescending(_ => int.Parse(_.ResourceWrapper.Version)), keepLastUpdated, useReplicasForReads);
                    loaded.AddRange(inputNoConflict);
                }
            }

            async Task Merge(IEnumerable<ImportResource> resources, bool keepLastUpdated, bool useReplicasForReads)
            {
                var input = resources.Select(_ => new ResourceWrapperOperation(_.ResourceWrapper, true, true, null, false, _.KeepVersion, null)).ToList();
                await MergeInternalAsync(input, keepLastUpdated, true, false, useReplicasForReads, eventualConsistency, false, cancellationToken);
            }
        }

        internal async Task MergeResourcesWrapperAsync(long transactionId, bool singleTransaction, IReadOnlyList<MergeResourceWrapper> mergeWrappers, bool enlistInTransaction, int timeoutRetries, IReadOnlyList<ResourceSearchParameterStatus> pendingStatuses, CancellationToken cancellationToken)
        {
            var sw = Stopwatch.StartNew();
            using var cmd = new SqlCommand();
            //// Do not use auto generated tvp generator as it does not allow to skip compartment tvp and paramters with default values
            cmd.CommandType = CommandType.StoredProcedure;

            if (pendingStatuses?.Count > 0)
            {
                cmd.CommandText = "dbo.MergeResourcesAndSearchParams";
                new SearchParamListTableValuedParameterDefinition("@SearchParams").AddParameter(cmd.Parameters, new SearchParamListRowGenerator().GenerateRows(pendingStatuses));
            }
            else
            {
                cmd.CommandText = "dbo.MergeResources";
                cmd.Parameters.AddWithValue("@SingleTransaction", singleTransaction);
            }

            cmd.Parameters.AddWithValue("@IsResourceChangeCaptureEnabled", _coreFeatures.SupportsResourceChangeCapture);
            cmd.Parameters.AddWithValue("@TransactionId", transactionId);

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
            var commandTimeout = 300 + (int)(3600.0 / 10000 * (timeoutRetries + 1) * mergeWrappers.Count);
            cmd.CommandTimeout = commandTimeout;

            if (enlistInTransaction && _sqlTransactionHandler.SqlTransactionScope != null)
            {
                using var conn = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, enlistInTransaction);
                cmd.Connection = conn.SqlConnection;
                cmd.Transaction = conn.SqlTransaction;
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }
            else
            {
                await cmd.ExecuteNonQueryAsync(_sqlRetryService, _logger, cancellationToken, disableRetries: true, applicationName: MergeApplicationName);
            }

            _logger.LogInformation($"MergeResourcesWrapperAsync: resources={mergeWrappers.Count}, searchParams={pendingStatuses?.Count ?? 0} transactionId={transactionId}, singleTransaction={singleTransaction}, enlistInTran={enlistInTransaction}, commandTimeout={commandTimeout}, elapsed={sw.Elapsed.TotalMilliseconds} ms.");
        }

        private void SetAndClearPendingSearchParameterStatus(ResourceWrapperOperation resource)
        {
            if (_requestContextAccessor?.RequestContext?.Properties?.TryGetValue(SearchParameterRequestContextPropertyNames.PendingStatus, out object value) == true)
            {
                resource.PendingSearchParameterStatus = (ResourceSearchParameterStatus)value;
                _requestContextAccessor.RequestContext.Properties.Remove(SearchParameterRequestContextPropertyNames.PendingStatus);
            }
        }

        public async Task<UpsertOutcome> UpsertAsync(ResourceWrapperOperation resource, CancellationToken cancellationToken)
        {
            bool isBundleParallelOperation =
                resource.BundleResourceContext != null &&
                resource.BundleResourceContext.IsParallelBundle;

            bool isBundleTransaction =
                resource.BundleResourceContext != null &&
                resource.BundleResourceContext.IsTransactionalBundle;

            // Extract pending statuses now so they are merged with the resource.
            // This is applicable for any bundle types.
            SetAndClearPendingSearchParameterStatus(resource);

            if (isBundleParallelOperation)
            {
                // Parallel operations:
                // - EnlistTransaction: should be always false, and rely on SQL transactions.
                IBundleOrchestratorOperation bundleOperation = _bundleOrchestrator.GetOperation(resource.BundleResourceContext.BundleOperationId);
                return await bundleOperation.AppendResourceAsync(resource, this, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                // Sequential operations:
                // - EnlistTransaction: set to true only in sequential transaction bundles (as they rely on C# transactions). Standalone operations should not enlist transactions (as they rely on SQL transactions).
                MergeOptions mergeOptions = new MergeOptions(
                    enlistTransaction: isBundleTransaction,
                    isBundleTransaction: isBundleTransaction);
                var mergeOutcome = await MergeAsync(new[] { resource }, mergeOptions, cancellationToken);
                DataStoreOperationOutcome dataStoreOperationOutcome = mergeOutcome.Results.First().Value;

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
            return await GetAsync(keys, false, true, cancellationToken); // do not return invisible records in public interface
        }

        private async Task<IReadOnlyList<ResourceWrapper>> GetAsync(IReadOnlyList<ResourceKey> keys, bool includeInvisible, bool isReadOnly, CancellationToken cancellationToken)
        {
            return await _sqlStoreClient.GetAsync(keys, _model.GetResourceTypeId, _compressedRawResourceConverter.ReadCompressedRawResource, _model.GetResourceTypeName, isReadOnly, cancellationToken, includeInvisible);
        }

        public async Task<ResourceWrapper> GetAsync(ResourceKey key, CancellationToken cancellationToken)
        {
            var results = await GetAsync(new[] { key }, cancellationToken);
            return results.Count == 0 ? null : results[0];
        }

        public async Task HardDeleteAsync(ResourceKey key, bool keepCurrentVersion, bool allowPartialSuccess, CancellationToken cancellationToken)
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

        private void ReplaceVersionId(ResourceWrapper resourceWrapper, string version)
        {
            resourceWrapper.Version = version;
            var currentVersion = GetJsonValue(resourceWrapper.RawResource.Data, "versionId", false);
            var rawResourceData = resourceWrapper.RawResource.Data.Replace($"\"versionId\":\"{currentVersion}\"", $"\"versionId\":\"{resourceWrapper.Version}\"", StringComparison.Ordinal);
            resourceWrapper.RawResource = new RawResource(rawResourceData, FhirResourceFormat.Json, true);
        }

        private void SyncVersionIdInMeta(ResourceWrapper resourceWrapper)
        {
            if (resourceWrapper.Version == InitialVersion) // version is already correct
            {
                return;
            }

            var version = GetJsonValue(resourceWrapper.RawResource.Data, "versionId", false);
            var rawResourceData = resourceWrapper.RawResource.Data.Replace($"\"versionId\":\"{version}\"", $"\"versionId\":\"{resourceWrapper.Version}\"", StringComparison.Ordinal);
            resourceWrapper.RawResource = new RawResource(rawResourceData, FhirResourceFormat.Json, true);
        }

        private void SyncVersionIdAndLastUpdatedInMeta(ResourceWrapper resourceWrapper)
        {
            var date = GetJsonValue(resourceWrapper.RawResource.Data, "lastUpdated", false);

            if (!resourceWrapper.RawResource.Data.Contains($"\"lastUpdated\":\"{date}\"", StringComparison.Ordinal))
            {
                _logger.LogWarning("Cannot parse lastUpdated value from input raw resource when trying to sync lastUpdated in meta.");
                throw new ArgumentException("Cannot parse lastUpdated value from input raw resource when trying to sync lastUpdated in meta.");
            }

            string rawResourceData;
            if (resourceWrapper.Version == InitialVersion) // version is already correct
            {
                rawResourceData = resourceWrapper.RawResource.Data
                                    .Replace($"\"lastUpdated\":\"{date}\"", $"\"lastUpdated\":\"{RemoveTrailingZerosFromMillisecondsForAGivenDate(resourceWrapper.LastModified)}\"", StringComparison.Ordinal);
            }
            else
            {
                var version = GetJsonValue(resourceWrapper.RawResource.Data, "versionId", false);

                if (!resourceWrapper.RawResource.Data.Contains($"\"versionId\":\"{version}\"", StringComparison.Ordinal))
                {
                    _logger.LogWarning("Cannot parse versionId value from input raw resource when trying to sync version in meta. Inserting version based on lastUpdated location.");
                    rawResourceData = resourceWrapper.RawResource.Data
                                        .Replace($"\"lastUpdated\":\"{date}\"", $"\"versionId\":\"{resourceWrapper.Version}\",\"lastUpdated\":\"{RemoveTrailingZerosFromMillisecondsForAGivenDate(resourceWrapper.LastModified)}\"", StringComparison.Ordinal);
                }
                else
                {
                    rawResourceData = resourceWrapper.RawResource.Data
                                        .Replace($"\"versionId\":\"{version}\"", $"\"versionId\":\"{resourceWrapper.Version}\"", StringComparison.Ordinal)
                                        .Replace($"\"lastUpdated\":\"{date}\"", $"\"lastUpdated\":\"{RemoveTrailingZerosFromMillisecondsForAGivenDate(resourceWrapper.LastModified)}\"", StringComparison.Ordinal);
                }
            }

            resourceWrapper.RawResource = new RawResource(rawResourceData, FhirResourceFormat.Json, true);
        }

        private void ThrowResourceVersionConflict(string expectedVersion)
        {
            _logger.LogInformation("PreconditionFailed: ResourceVersionConflict");
            throw new PreconditionFailedException(string.Format(Core.Resources.ResourceVersionConflict, expectedVersion));
        }

        /// <summary>
        /// Authoritatively re-verifies the caller supplied version preconditions of an operation that has been reduced
        /// to a logical no-op (a delete of an already deleted resource, or an update whose content is byte identical to
        /// what is stored).
        /// </summary>
        /// <remarks>
        /// <para>
        /// Every other version comparison in a merge is ultimately enforced by dbo.MergeResources: the stored procedure
        /// re-reads the current version inside the same transaction that performs the write and raises a conflict when
        /// it no longer matches, and the unique index on (ResourceTypeId, ResourceId, Version) makes that decision
        /// atomic with the write. A no-op sends nothing to the stored procedure, so it never gets that protection. The
        /// batch snapshot taken at the top of <see cref="MergeInternalAsync"/> cannot substitute for it: it is an
        /// unlocked, point-in-time read taken several round trips earlier and, under READ_COMMITTED_SNAPSHOT (the
        /// Azure SQL default), it returns a row version from before a concurrent writer's uncommitted change and does
        /// not block on that writer.
        /// </para>
        /// <para>
        /// This check restores an equivalent guarantee by re-reading the current row with UPDLOCK on the primary,
        /// which is enough to force the read to observe a concurrent writer of the same, already-matched row: that
        /// writer either committed before the lock was granted - in which case the new version is observed here and
        /// the precondition fails - or is forced to wait behind it, which serializes the no-op ahead of that writer.
        /// HOLDLOCK is deliberately not added: paired with UPDLOCK it would escalate to a serializable key-range lock,
        /// and dbo.MergeResources documents that exact combination as deadlock prone on its own, analogous version
        /// comparison join (see the commented out hint in MergeResources.sql), without buying anything here since the
        /// row being probed is already known to exist. It is the SQL Server counterpart of the ETag guarded write
        /// Cosmos DB uses for the same decision, and it deliberately performs no write, so a no-op still creates no
        /// FHIR version. When the merge is enlisted in an ambient transaction (a sequential transaction bundle), the
        /// read runs on that transaction and its lock is held until the bundle's write boundary commits, making the
        /// check atomic with the bundle.
        /// </para>
        /// <para>
        /// The caller supplied WeakETag is normalized the same way as the batch snapshot comparison earlier in
        /// <see cref="MergeInternalAsync"/> (see <see cref="ParseWeakETagVersionOrSentinel"/>), so a non-canonical
        /// numeric ETag such as <c>W/"01"</c> is still recognized as matching a stored version of <c>"1"</c>; only the
        /// comparison is normalized; the version reported in a failure is always the caller's original value.
        /// </para>
        /// <para>
        /// Unguarded no-ops are unaffected and issue no additional query.
        /// </para>
        /// </remarks>
        /// <param name="resourceExt">The operation whose preconditions must be re-verified.</param>
        /// <param name="enlistInTransaction">Whether this merge is enlisted in an ambient SQL transaction.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The failure to report for this operation, or <c>null</c> when every precondition still holds.</returns>
        private async Task<PreconditionFailedException> VerifyNoOpVersionPreconditionAsync(ResourceWrapperOperation resourceExt, bool enlistInTransaction, CancellationToken cancellationToken)
        {
            string rawETagVersion = resourceExt.WeakETag?.VersionId;
            string comparedVersion = resourceExt.ComparedVersion;

            if (rawETagVersion == null && comparedVersion == null)
            {
                return null;
            }

            // Normalized the same way as the batch snapshot comparison above (ParseWeakETagVersionOrSentinel), so a
            // non-canonical numeric ETag like "01" still matches a stored "1" instead of failing this no-op's
            // precondition purely because it happened to take this authoritative, re-read path.
            string normalizedETagVersion = rawETagVersion == null
                ? null
                : ParseWeakETagVersionOrSentinel(rawETagVersion).ToString(CultureInfo.InvariantCulture);

            IReadOnlyDictionary<(short ResourceTypeId, string ResourceId), ResourceDateKey> current =
                await ReadCurrentResourceVersionsAsync(new[] { resourceExt }, acquireUpdateLocks: true, enlistInTransaction, cancellationToken);

            //// A missing entry means the guarded target disappeared entirely (for example a concurrent hard delete or
            //// purge) after it was matched. string.Equals with a null current version therefore reports a failed
            //// precondition rather than a not-found error, matching Cosmos DB's behavior for a guarded disappearance.
            current.TryGetValue(GetVersionProbeKey(resourceExt), out ResourceDateKey authoritative);
            string currentVersion = authoritative?.VersionId;

            if (rawETagVersion != null && !string.Equals(normalizedETagVersion, currentVersion, StringComparison.Ordinal))
            {
                _logger.LogInformation("PreconditionFailed: ResourceVersionConflict");
                return new PreconditionFailedException(string.Format(Core.Resources.ResourceVersionConflict, rawETagVersion));
            }

            if (comparedVersion != null && !string.Equals(comparedVersion, currentVersion, StringComparison.Ordinal))
            {
                _logger.LogInformation("PreconditionFailed: ResourceVersionConflict");
                return new PreconditionFailedException(string.Format(Core.Resources.ResourceVersionConflict, comparedVersion));
            }

            return null;
        }

        /// <summary>
        /// Parses a caller supplied WeakETag version id the same way for every version comparison in a merge: an
        /// integer version string compares by value (so a non-canonical form like <c>"01"</c> matches a stored
        /// <c>"1"</c>), and anything that is not a valid integer is mapped to a sentinel value that can never match a
        /// real stored version, so it still fails as a precondition mismatch rather than silently comparing equal to
        /// nothing.
        /// </summary>
        /// <param name="versionId">The WeakETag version id to parse. Must not be <c>null</c>.</param>
        /// <returns>The parsed version, or <c>-1</c> when <paramref name="versionId"/> is not a valid integer.</returns>
        private static int ParseWeakETagVersionOrSentinel(string versionId)
        {
            return int.TryParse(versionId, out var parsedVersion) ? parsedVersion : -1;
        }

        /// <summary>
        /// Determines which operation in a merge batch, if any, actually violated its caller supplied version
        /// precondition when dbo.MergeResources reported a conflict.
        /// </summary>
        /// <remarks>
        /// The stored procedure raises a single, generic conflict for the whole batch. That conflict can originate from
        /// an operation that carries no precondition at all - for example a plain create whose surrogate id ordering
        /// collided - so attributing it to whichever guarded operation happens to appear first would turn an unrelated
        /// batch failure into a 412 against a resource that is still exactly where the client left it. This re-reads
        /// the guarded operations and returns only one whose comparison genuinely no longer matches the database. The
        /// read is deliberately non-locking and runs on its own connection: the ambient transaction of a sequential
        /// transaction bundle may still hold write locks at this point and is itself unusable after the conflict, so a
        /// locking probe could block on the caller's own transaction.
        /// <para>
        /// The comparison against the authoritative version is normalized the same way as the batch snapshot
        /// comparison and the guarded no-op probe (see <see cref="ParseWeakETagVersionOrSentinel"/>), so a
        /// non-canonical numeric ETag such as <c>W/"01"</c> is still recognized as matching a stored version of
        /// <c>"1"</c> and is not falsely correlated to an unrelated conflict elsewhere in the batch.
        /// </para>
        /// </remarks>
        /// <param name="resources">The operations submitted in the failing merge batch.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The operation whose precondition failed, or <c>null</c> when that cannot be established.</returns>
        private async Task<ResourceWrapperOperation> FindOperationWithViolatedVersionPreconditionAsync(IReadOnlyList<ResourceWrapperOperation> resources, CancellationToken cancellationToken)
        {
            List<ResourceWrapperOperation> guarded = resources.Where(resource => resource.WeakETag != null || resource.ComparedVersion != null).ToList();
            if (guarded.Count == 0)
            {
                return null;
            }

            // A single operation merge contains no other operation the conflict could be attributed to.
            if (resources.Count == 1)
            {
                return guarded[0];
            }

            try
            {
                IReadOnlyDictionary<(short ResourceTypeId, string ResourceId), ResourceDateKey> current =
                    await ReadCurrentResourceVersionsAsync(guarded, acquireUpdateLocks: false, enlistInTransaction: false, cancellationToken);

                return guarded.FirstOrDefault(resource =>
                {
                    // Normalized the same way as the batch snapshot comparison and the guarded no-op probe
                    // (ParseWeakETagVersionOrSentinel), so a non-canonical numeric WeakETag like W/"01" is still
                    // recognized as matching a stored version of "1" here too; comparing the raw client string
                    // would otherwise falsely correlate an unrelated batch conflict to this operation as a
                    // spurious 412.
                    string comparisonVersion = resource.WeakETag != null
                        ? ParseWeakETagVersionOrSentinel(resource.WeakETag.VersionId).ToString(CultureInfo.InvariantCulture)
                        : resource.ComparedVersion;
                    return !current.TryGetValue(GetVersionProbeKey(resource), out ResourceDateKey authoritative)
                        || !string.Equals(comparisonVersion, authoritative.VersionId, StringComparison.Ordinal);
                });
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // Correlation is a best effort classification of a failure that has already happened. If it cannot be
                // performed, reporting the generic conflict is strictly safer than naming an operation whose
                // precondition was never confirmed to have failed, so the caller falls back to its retry handling.
                _logger.LogWarning(e, "Unable to correlate a SQL conflict to a specific version precondition.");
                return null;
            }
        }

        /// <summary>
        /// Reads the authoritative current version of the supplied operations' resources from the primary replica.
        /// </summary>
        /// <param name="operations">The operations whose resources should be probed.</param>
        /// <param name="acquireUpdateLocks">Whether the probe should acquire update and range locks.</param>
        /// <param name="enlistInTransaction">Whether the probe should run on the ambient transaction, when one exists.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The current state of each probed resource that exists, keyed by resource type id and resource id.</returns>
        private async Task<IReadOnlyDictionary<(short ResourceTypeId, string ResourceId), ResourceDateKey>> ReadCurrentResourceVersionsAsync(
            IReadOnlyList<ResourceWrapperOperation> operations,
            bool acquireUpdateLocks,
            bool enlistInTransaction,
            CancellationToken cancellationToken)
        {
            List<ResourceKeyListRow> keys = operations
                .Select(GetVersionProbeKey)
                .Distinct()
                .Select(key => new ResourceKeyListRow(key.ResourceTypeId, key.ResourceId, null))
                .ToList();

            SqlConnectionWrapper enlistedConnection = null;
            try
            {
                if (enlistInTransaction && _sqlTransactionHandler.SqlTransactionScope != null)
                {
                    enlistedConnection = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true);
                }

                IReadOnlyList<ResourceDateKey> current = await StoreClient.GetCurrentResourceVersionsAsync(keys, acquireUpdateLocks, enlistedConnection, cancellationToken);
                return current.ToDictionary(resource => (resource.ResourceTypeId, resource.Id));
            }
            finally
            {
                enlistedConnection?.Dispose();
            }
        }

        private (short ResourceTypeId, string ResourceId) GetVersionProbeKey(ResourceWrapperOperation operation)
        {
            return (_model.GetResourceTypeId(operation.Wrapper.ResourceTypeName), operation.Wrapper.ResourceId);
        }

        private bool ExistingRawResourceIsEqualToInput(RawResource input, RawResource existing, bool keepVersion)
        {
            if (!_rawResourceDeduping.IsEnabled(_sqlRetryService))
            {
                return false;
            }

            if (keepVersion)
            {
                return input.Data == existing.Data;
            }

            var inputDate = GetJsonValue(input.Data, "lastUpdated", false);
            var inputVersion = GetJsonValue(input.Data, "versionId", true);
            var existingDate = GetJsonValue(existing.Data, "lastUpdated", true);
            var existingVersion = GetJsonValue(existing.Data, "versionId", true);
            if (inputVersion == existingVersion)
            {
                if (inputDate == existingDate)
                {
                    return input.Data == existing.Data;
                }

                return input.Data == existing.Data.Replace($"\"lastUpdated\":\"{existingDate}\"", $"\"lastUpdated\":\"{inputDate}\"", StringComparison.Ordinal);
            }
            else
            {
                if (inputDate == existingDate)
                {
                    return input.Data == existing.Data.Replace($"\"versionId\":\"{existingVersion}\"", $"\"versionId\":\"{inputVersion}\"", StringComparison.Ordinal);
                }

                return input.Data
                            == existing.Data
                                .Replace($"\"versionId\":\"{existingVersion}\"", $"\"versionId\":\"{inputVersion}\"", StringComparison.Ordinal)
                                .Replace($"\"lastUpdated\":\"{existingDate}\"", $"\"lastUpdated\":\"{inputDate}\"", StringComparison.Ordinal);
            }
        }

        private static bool ChangesAreOnlyInMetadata(ResourceWrapper inputWrapper, ResourceWrapper existingWrapper)
        {
            var inputData = inputWrapper.RawResource.Data;
            var existingData = existingWrapper.RawResource.Data;

            var inputMetaStartIndex = inputData.IndexOf("\"meta\":", StringComparison.Ordinal);
            var existingMetaStartIndex = existingData.IndexOf("\"meta\":", StringComparison.Ordinal);

            var inputDataWithoutMeta = inputData;
            var existingDataWithoutMeta = existingData;

            if (inputMetaStartIndex != -1)
            {
                var inputMeta = inputData.GetJsonSection(inputMetaStartIndex);
                inputDataWithoutMeta = inputData.Replace(inputMeta, string.Empty, StringComparison.Ordinal);
            }

            if (existingMetaStartIndex != -1)
            {
                var existingMeta = existingData.GetJsonSection(existingMetaStartIndex);
                existingDataWithoutMeta = existingData.Replace(existingMeta, string.Empty, StringComparison.Ordinal);
            }

            return inputDataWithoutMeta.Equals(existingDataWithoutMeta, StringComparison.Ordinal);
        }

        // This method relies on current raw resource string formatting, i.e. no extra spaces.
        // This logic should be removed once "resource.meta not available" bug is fixed.
        private string GetJsonValue(string json, string propName, bool isExisting)
        {
            var startIndex = json.IndexOf($"\"{propName}\":\"", StringComparison.Ordinal);
            if (startIndex == -1)
            {
                // I think this should be a warning because it happens every time a resource is deleted. Maybe even info.
                _logger.LogWarning($"Cannot parse {propName} value from {(isExisting ? "existing" : "input")}");
                return string.Empty;
            }

            startIndex = startIndex + propName.Length + 4;
            var endIndex = json.IndexOf('"', startIndex);
            if (endIndex == -1)
            {
                _logger.LogWarning($"Cannot parse {propName} value from {(isExisting ? "existing" : "input")}");
                return string.Empty;
            }

            var value = json.Substring(startIndex, endIndex - startIndex);

            return value;
        }

        public async Task BuildAsync(ICapabilityStatementBuilder builder, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(builder, nameof(builder));

            await builder.PopulateDefaultResourceInteractions()
                .SyncSearchParameters()
                .AddGlobalSearchParameters()
                .SyncProfilesAsync(cancellationToken);

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
    }
}
