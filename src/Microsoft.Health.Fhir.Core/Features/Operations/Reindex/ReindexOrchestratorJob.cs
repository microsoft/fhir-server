// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.Model;
using Hl7.Fhir.Utility;
using Microsoft.AspNetCore.JsonPatch.Internal;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Abstractions.Exceptions;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Operations.Reindex.Models;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Core.Registration;
using Microsoft.Health.JobManagement;
using Newtonsoft.Json;
using Polly;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Reindex
{
    [JobTypeId((int)JobType.ReindexOrchestrator)]
    public sealed class ReindexOrchestratorJob : IJob
    {
        private ILogger<ReindexOrchestratorJob> _logger;
        private readonly Func<IScoped<ISearchService>> _searchServiceFactory;
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager;
        private readonly ISearchParameterStatusManager _searchParameterStatusManager;
        private readonly IModelInfoProvider _modelInfoProvider;
        private readonly ISearchParameterOperations _searchParameterOperations;
        private readonly bool _isSurrogateIdRangingSupported;
        private readonly OperationsConfiguration _operationsConfiguration;
        private readonly int _searchParameterCacheRefreshIntervalSeconds;

        private CancellationToken _cancellationToken;
        private IQueueClient _queueClient;
        private JobInfo _jobInfo;
        private ReindexOrchestratorJobDefinition _definition;
        private readonly Dictionary<string, SearchResultReindex> _resourceCounts = [];
        private ReindexOrchestratorJobResult _result;
        private static readonly AsyncPolicy _timeoutRetries = Policy
            .Handle<SqlException>(ex => ex.IsExecutionTimeout())
            .WaitAndRetryAsync(3, _ => TimeSpan.FromMilliseconds(RandomNumberGenerator.GetInt32(1000, 5000)));

        /// <summary>
        /// Retry policy for Cosmos DB 429 (TooManyRequests) errors.
        /// Uses the RetryAfter hint from Cosmos DB if available, otherwise waits 1-5 seconds.
        /// </summary>
        private static readonly AsyncPolicy _requestRateRetries = Policy
            .Handle<RequestRateExceededException>()
            .WaitAndRetryAsync(
                3,
                (retryAttempt, exception, context) =>
                {
                    var rrException = exception as RequestRateExceededException;
                    return rrException?.RetryAfter ?? TimeSpan.FromMilliseconds(RandomNumberGenerator.GetInt32(1000, 5000));
                },
                (exception, timeSpan, retryAttempt, context) => Task.CompletedTask);

        /// <summary>
        /// Combined retry policy for search parameter status updates.
        /// Handles both SQL Server timeouts and Cosmos DB 429 errors.
        /// </summary>
        private static readonly AsyncPolicy _retries = Policy.WrapAsync(_requestRateRetries, _timeoutRetries);

        private readonly HashSet<string> _processedSearchParameters = []; // to prevent multiple status updates

        // Transient dictionaries below are populated on processing job creates. After a job is in the terminal state
        // it is removed from _transientResourceTypeJobs. When all jobs removed then resource type is completed.
        // Similar concept is used for _transientSearchParamResouceTypes
        private readonly Dictionary<string, (HashSet<long> JobIds, Counts Counts)> _transientResourceTypeJobs = [];
        private readonly Dictionary<string, (HashSet<string> ResourceTypes, SearchParameterStatus Status)> _transientSearchParamResouceTypes = [];
        //// populated with holds enqueued job ids. job is removed after it is finished (terminal state, completed or failed).
        private readonly SortedSet<long> _transientProcessingJobIds = [];

        private DateTimeOffset _searchParamLastUpdated;

        public ReindexOrchestratorJob(
            IQueueClient queueClient,
            Func<IScoped<ISearchService>> searchServiceFactory,
            ISearchParameterDefinitionManager searchParameterDefinitionManager,
            IModelInfoProvider modelInfoProvider,
            ISearchParameterStatusManager searchParameterStatusManager,
            ISearchParameterOperations searchParameterOperations,
            IFhirRuntimeConfiguration fhirRuntimeConfiguration,
            ILoggerFactory loggerFactory,
            IOptions<CoreFeatureConfiguration> coreFeatureConfiguration,
            IOptions<OperationsConfiguration> operationsConfiguration)
        {
            EnsureArg.IsNotNull(queueClient, nameof(queueClient));
            EnsureArg.IsNotNull(searchServiceFactory, nameof(searchServiceFactory));
            EnsureArg.IsNotNull(loggerFactory, nameof(loggerFactory));
            EnsureArg.IsNotNull(searchParameterDefinitionManager, nameof(searchParameterDefinitionManager));
            EnsureArg.IsNotNull(modelInfoProvider, nameof(modelInfoProvider));
            EnsureArg.IsNotNull(searchParameterStatusManager, nameof(searchParameterStatusManager));
            EnsureArg.IsNotNull(searchParameterOperations, nameof(searchParameterOperations));
            EnsureArg.IsNotNull(coreFeatureConfiguration, nameof(coreFeatureConfiguration));
            EnsureArg.IsNotNull(coreFeatureConfiguration.Value, nameof(coreFeatureConfiguration.Value));
            EnsureArg.IsNotNull(operationsConfiguration, nameof(operationsConfiguration));
            EnsureArg.IsNotNull(operationsConfiguration.Value, nameof(operationsConfiguration.Value));

            _queueClient = queueClient;
            _searchServiceFactory = searchServiceFactory;
            _logger = loggerFactory.CreateLogger<ReindexOrchestratorJob>();
            _searchParameterDefinitionManager = searchParameterDefinitionManager;
            _modelInfoProvider = modelInfoProvider;
            _searchParameterStatusManager = searchParameterStatusManager;
            _searchParameterOperations = searchParameterOperations;
            _operationsConfiguration = operationsConfiguration.Value;
            _searchParameterCacheRefreshIntervalSeconds = coreFeatureConfiguration.Value.SearchParameterCacheRefreshIntervalSeconds;

            // Determine support for surrogate ID ranging once
            // This is to ensure Gen1 Reindex still works as expected but we still maintain perf on job inseration to SQL
            _isSurrogateIdRangingSupported = fhirRuntimeConfiguration.IsSurrogateIdRangingSupported;
            _logger.LogInformation(_isSurrogateIdRangingSupported ? "Using SQL Server search service with surrogate ID ranging support" : "Using search service without surrogate ID ranging support (likely Cosmos DB)");
        }

        public async Task<string> ExecuteAsync(JobInfo jobInfo, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(jobInfo, nameof(jobInfo));
            _jobInfo = jobInfo;
            _result = new ReindexOrchestratorJobResult();
            _definition = JsonConvert.DeserializeObject<ReindexOrchestratorJobDefinition>(_jobInfo.Definition);
            _cancellationToken = cancellationToken; // TODO: Do we need cancel?

            try
            {
                await RefreshSearchParameterCache(true);

                _logger.LogInformation("Reindex job with Id: {Id} has been started. Status: {Status}.", _jobInfo.Id, _jobInfo.Status);

                var currentJobs = new List<JobInfo>();
                if (!_isSurrogateIdRangingSupported) // get all jobs only for cosmos as in sql number of jobs can be large and call can timeout
                {
                    var jobs = await _queueClient.GetJobByGroupIdAsync((byte)QueueType.Reindex, _jobInfo.GroupId, true, cancellationToken);
                    currentJobs = jobs.Where(j => j.Id != _jobInfo.GroupId).ToList();
                }

                // For SQL Server, always attempt job creation - we use Export-style resume logic
                // to calculate remaining work from existing jobs, preventing duplicates.
                // For Cosmos, use the existing binary check since job definitions don't have unique ranges.
                if (_isSurrogateIdRangingSupported || !currentJobs.Any())
                {
                    await CreateReindexProcessingJobsAsync(cancellationToken);
                }
                else // cosmos job restart
                {
                    foreach (var job in currentJobs.Select(_ => new { _.Id, Def = JsonConvert.DeserializeObject<ReindexProcessingJobDefinition>(_.Definition) }))
                    {
                        PopulateProcessingLookups(job.Def.ResourceType, job.Def.SearchParameterUrlStatuses, [job.Id]);
                    }
                }

                _result.CreatedJobs = currentJobs.Count; // TODO: Move this logic inside create

                await CheckForCompletionAsync(cancellationToken);

                await RefreshSearchParameterCache(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogJobInformation(jobInfo, $"The reindex job was cancelled by caller, Id={_jobInfo.Id}");
                AddErrorResult(OperationOutcomeConstants.IssueSeverity.Information, OperationOutcomeConstants.IssueType.Informational, Core.Resources.ReindexingCancelledbyCaller);
            }
            catch (OperationCanceledException ex)
            {
                _logger.LogJobError(ex, _jobInfo, $"The reindex job was canceled, Id={_jobInfo.Id}");
                AddErrorResult(OperationOutcomeConstants.IssueSeverity.Error, OperationOutcomeConstants.IssueType.Incomplete, Core.Resources.ReindexingJobCancelled);
            }
            catch (Exception ex)
            {
                AddErrorResult(OperationOutcomeConstants.IssueSeverity.Error, OperationOutcomeConstants.IssueType.Exception, ex.Message);
                _logger.LogJobError(ex, _jobInfo, $"The reindex failed. Id={_jobInfo.Id}");
            }

            return JsonConvert.SerializeObject(_result);
        }

        private async Task RefreshSearchParameterCache(bool isReindexStart)
        {
            var suffix = isReindexStart ? "Start" : "End";
            _logger.LogJobInformation(_jobInfo, $"Reindex orchestrator job started cache refresh at the {suffix}.");
            await TryLogEvent($"ReindexOrchestratorJob={_jobInfo.Id}.ExecuteAsync.{suffix}", "Warn", "Started", null, _cancellationToken);

            if (_isSurrogateIdRangingSupported)
            {
                // SQL Server: Wait for all instances to update their cache. This prevents the
                // orchestrator from creating reindex ranges while other instances still have
                // stale search parameter caches and would write resources with wrong hashes.
                var updateEventsSince = isReindexStart ? _jobInfo.StartDate.Value : DateTime.UtcNow;
                var isConsistent = await WaitForAllInstancesCacheSyncAsync(updateEventsSince, _cancellationToken);
                if (!isConsistent)
                {
                    var msg = "Unable to sync search parameter cache. Please resubmit reindex. If issue persists please contact your administrator.";
                    _logger.LogJobError(_jobInfo, msg);
                    await TryLogEvent($"ReindexOrchestratorJob={_jobInfo.Id}.ExecuteAsync.{suffix}", "Error", msg, null, _cancellationToken);
                    throw new JobExecutionException(msg, false);
                }
            }
            else
            {
                // Cosmos DB: There is no EventLog-based convergence tracking, so wait a fixed
                // delay to allow all instances to refresh their search parameter caches.
                var delayMs = _operationsConfiguration.Reindex.CacheRefreshWaitMultiplier * _searchParameterCacheRefreshIntervalSeconds * 1000;
                _logger.LogJobInformation(_jobInfo, $"Cosmos DB detected — waiting {delayMs}ms for cache propagation across instances.");
                await Task.Delay(delayMs, _cancellationToken);
            }

            _searchParamLastUpdated = _searchParameterOperations.SearchParamLastUpdated;

            _logger.LogJobInformation(_jobInfo, $"Reindex orchestrator job completed cache refresh at the {suffix}: SearchParamLastUpdated {_searchParamLastUpdated}");
            await TryLogEvent($"ReindexOrchestratorJob={_jobInfo.Id}.ExecuteAsync.{suffix}", "Warn", $"SearchParamLastUpdated={_searchParamLastUpdated.ToString("yyyy-MM-dd HH:mm:ss.fff")}", null, _cancellationToken);

            async Task<bool> WaitForAllInstancesCacheSyncAsync(DateTime updateEventsSince, CancellationToken cancellationToken)
            {
                var start = Stopwatch.StartNew();
                var maxWaitTime = TimeSpan.FromSeconds(_operationsConfiguration.Reindex.CacheUpdateMaxWaitMultiplier * _searchParameterCacheRefreshIntervalSeconds);
                var waitInterval = TimeSpan.FromSeconds(_searchParameterCacheRefreshIntervalSeconds);
                CacheConsistencyResult result = null;
                while (start.Elapsed < maxWaitTime)
                {
                    var activeHostsSince = DateTime.UtcNow.AddSeconds((-1) * _operationsConfiguration.Reindex.ActiveHostsEventsMultiplier * _searchParameterCacheRefreshIntervalSeconds);
                    result = await _searchParameterStatusManager.CheckCacheConsistencyAsync(updateEventsSince, activeHostsSince, cancellationToken);

                    if (result.IsConsistent)
                    {
                        _logger.LogJobInformation(_jobInfo, $"Cache sync check: All {result.ActiveHosts} active host(s) have converged to SearchParamLastUpdated={_searchParameterOperations.SearchParamLastUpdated.ToString("yyyy-MM-dd HH:mm:ss.fff")}.");
                        break;
                    }

                    _logger.LogJobInformation(_jobInfo, $"Cache sync check: {result.ConvergedHosts}/{result.ActiveHosts} hosts synced. Waiting...");
                    await Task.Delay(waitInterval, cancellationToken);
                }

                return result != null && result.IsConsistent;
            }
        }

        private async Task<List<string>> CleanupMissingSearchParameterResourcesAsync(IReadOnlyCollection<ResourceSearchParameterStatus> allStatuses, CancellationToken cancellationToken)
        {
            _logger.LogJobInformation(_jobInfo, "Checking for search parameters in pending delete states with missing resources.");

            var pending = allStatuses.Where(sp => sp.Status == SearchParameterStatus.PendingDelete || sp.Status == SearchParameterStatus.PendingHardDelete).Select(sp => sp.Uri.OriginalString).ToList();
            _logger.LogJobInformation(_jobInfo, "Found {Count} search parameter(s) in pending delete states. Checking if resources exist.", pending.Count);

            var searchParameters = await _retries.ExecuteAsync(async () => await _searchParameterOperations.GetSearchParametersByUrlsAsync(pending, cancellationToken));

            var toMarkDeleted = new List<string>();
            foreach (var url in pending.Where(url => !searchParameters.ContainsKey(url)))
            {
                _logger.LogJobInformation(_jobInfo, "Search parameter resource '{Url}' not found - will mark as Deleted.", url);
                toMarkDeleted.Add(url);
            }

            if (toMarkDeleted.Any())
            {
                _logger.LogJobInformation(_jobInfo, "Marking {Count} search parameter(s) as Deleted due to missing resources.", toMarkDeleted.Count);
                await _retries.ExecuteAsync(
                    async () => await _searchParameterStatusManager.UpdateSearchParameterStatusAsync(toMarkDeleted, SearchParameterStatus.Deleted, cancellationToken, reindexId: _jobInfo.Id));
            }

            return toMarkDeleted;
        }

        private async Task<IReadOnlyList<long>> CreateReindexProcessingJobsAsync(CancellationToken cancellationToken)
        {
            // Build queries based on new search params
            // Find search parameters not in a final state such as supported, pendingDelete, pendingDisable.
            var targetStatuses = new List<SearchParameterStatus>() { SearchParameterStatus.Supported, SearchParameterStatus.PendingDelete, SearchParameterStatus.PendingHardDelete, SearchParameterStatus.PendingDisable };
            var initial = await _searchParameterStatusManager.GetAllSearchParameterStatus(cancellationToken);

            // Clean up search parameters in pending delete states if resources don't exist
            var deleted = await CleanupMissingSearchParameterResourcesAsync(initial, cancellationToken);

            // Get all URIs that have at least one entry with a valid status
            // Exclude search parameters marked as deleted during cleanup
            var initialMinusDeleted = initial
                                        .Where(s => targetStatuses.Contains(s.Status))
                                        .Where(s => !deleted.Contains(s.Uri.OriginalString))
                                        .GroupBy(s => s.Uri.OriginalString, StringComparer.Ordinal)
                                        .ToDictionary(g => g.Key, g => g.First().Status, StringComparer.Ordinal);

            // Filter to only those search parameters which have valid definitions
            var targetParams = new List<SearchParameterInfo>();
            foreach (var validUri in initialMinusDeleted.Keys)
            {
                if (_searchParameterDefinitionManager.TryGetSearchParameter(validUri, out var param))
                {
                    targetParams.Add(param);
                    var msg = $"status={param.SearchParameterStatus} uri={validUri}";
                    _logger.LogJobInformation(_jobInfo, msg);
                    await TryLogEvent($"ReindexOrchestratorJob={_jobInfo.Id}.GetDefinitionFromCache", "Warn", msg, null, cancellationToken);
                }
                else
                {
                    // TODO: We should throw here in the next phase otherwise we will reindex incorrectly
                    var msg = $"status=null uri={validUri}";
                    _logger.LogJobWarning(_jobInfo, msg);
                    await TryLogEvent($"ReindexOrchestratorJob={_jobInfo.Id}.GetDefinitionFromCache", "Error", msg, null, cancellationToken);
                }
            }

            var resourceTypes = new HashSet<string>();
            var usedResourceTypes = await GetUsedResourceTypes(cancellationToken);

            // From the target params, get the list of necessary resource types
            foreach (var param in targetParams)
            {
                var paramResourceTypes = _searchParameterDefinitionManager.GetDerivedResourceTypes(param.BaseResourceTypes).ToList();

                // to support no matching resources case register all resource types in the transient lookups
                foreach (var resourceType in paramResourceTypes)
                {
                    PopulateProcessingLookups(resourceType, [(param.Url.OriginalString, initialMinusDeleted[param.Url.OriginalString])], new List<long>());
                }

                // exclude not used resource types from enqueueing. this also removes resource types which we do not have id mapping for (like Resource).
                paramResourceTypes = paramResourceTypes.Where(_ => usedResourceTypes.Contains(_)).ToList();
                resourceTypes.UnionWith(paramResourceTypes);
            }

            // if there are not any parameters which are supported but not yet indexed, then we have nothing to do
            if (targetParams.Count == 0 && resourceTypes.Count == 0)
            {
                AddErrorResult(OperationOutcomeConstants.IssueSeverity.Information, OperationOutcomeConstants.IssueType.Informational, string.Format(Core.Resources.ReindexingNoSearchParameterstoReindex, _jobInfo.Id));
                return new List<long>();
            }

            if (!_isSurrogateIdRangingSupported) // only cosmos needs resource counts to support chunking
            {
                await CalculateAndSetTotalAndResourceCounts(resourceTypes);
            }

            return await EnqueueQueryProcessingJobsAsync(resourceTypes, cancellationToken);
        }

        private void AddErrorResult(string severity, string issueType, string message)
        {
            var errorList = new List<OperationOutcomeIssue> { new OperationOutcomeIssue(severity, issueType, message) };
            errorList.AddRange(_result.Error);
            _result.Error = errorList;
        }

        private async Task<HashSet<string>> GetUsedResourceTypes(CancellationToken cancellationToken)
        {
            using var searchService = _searchServiceFactory();
            var resourceTypes = new HashSet<string>(await searchService.Value.GetUsedResourceTypes(cancellationToken));
            return resourceTypes;
        }

        private async Task<IReadOnlyList<long>> EnqueueQueryProcessingJobsAsync(HashSet<string> resourceTypes, CancellationToken cancellationToken)
        {
            var resourcesPerJob = (int)_definition.MaximumNumberOfResourcesPerQuery;
            var allEnqueuedJobIds = new List<long>();

            foreach (var resourceType in resourceTypes)
            {
                var searchParams = _transientSearchParamResouceTypes.Where(_ => _.Value.ResourceTypes.Contains(resourceType)).Select(_ => (Url: _.Key, _.Value.Status)).ToList();
                PopulateProcessingLookups(resourceType, searchParams, new List<long>());
                var urlsToProcess = searchParams.Select(_ => _.Url).ToList();

                var totalRangesEnqueued = 0;

                if (_isSurrogateIdRangingSupported)
                {
                    // Use batched calls to GetSurrogateIdRanges to avoid timeout on large tables
                    // Enqueue each batch immediately so workers can start processing sooner
                    var numberOfRangesPerBatch = _operationsConfiguration.Reindex.NumberOfRecordRanges;
                    var startId = 0L;
                    var endId = long.MaxValue;

                    _logger.LogJobInformation(_jobInfo, "Fetching and enqueueing surrogate ID ranges for resource type {ResourceType} in batches of {BatchSize}. StartId={StartId}, EndId={EndId}", resourceType, numberOfRangesPerBatch, startId, endId);

                    using var searchService = _searchServiceFactory();
                    IReadOnlyList<(long StartId, long EndId, int Count)> ranges;
                    do
                    {
                        ranges = await searchService.Value.GetSurrogateIdRanges(resourceType, startId, endId, resourcesPerJob, numberOfRangesPerBatch, true, cancellationToken, true);
                        if (ranges.Any())
                        {
                            var batchJobIds = await CreateAndEnqueueJobDefinitionsAsync(ranges, resourceType, searchParams, cancellationToken);

                            PopulateProcessingLookups(resourceType, searchParams, batchJobIds);

                            allEnqueuedJobIds.AddRange(batchJobIds);
                            totalRangesEnqueued += ranges.Count;

                            startId = ranges[^1].EndId + 1; // Move past the last range
                        }
                    }
                    while (ranges.Any());

                    _logger.LogJobInformation(_jobInfo, "Completed fetching and enqueueing {RangeCount} surrogate ID ranges for resource type {ResourceType}.", totalRangesEnqueued, resourceType);
                }
                else
                {
                    // Create uniform-sized chunks based on resource count
                    var resourceCount = _resourceCounts[resourceType]; // Resource counts are calculated only for Cosmos
                    var numberOfChunks = Math.Max(1, (int)Math.Ceiling(resourceCount.Count / (double)resourcesPerJob)); // create at least one chunk even if count is zero
                    _logger.LogJobInformation(_jobInfo, "Using calculated ranges for resource type {ResourceType}. Creating {Count} chunks.", resourceType, numberOfChunks);
                    var processingRanges = new List<(long StartId, long EndId, int Count)>();
                    for (var i = 0; i < numberOfChunks; i++)
                    {
                        processingRanges.Add((0, 0, 0));
                    }

                    var batchJobIds = await CreateAndEnqueueJobDefinitionsAsync(processingRanges, resourceType, searchParams, cancellationToken);

                    PopulateProcessingLookups(resourceType, searchParams, batchJobIds);

                    allEnqueuedJobIds.AddRange(batchJobIds);
                }

                _logger.LogJobInformation(_jobInfo, "Created jobs for resource type {ResourceType} with {Count} valid search parameters: {SearchParams}", resourceType, urlsToProcess.Count, string.Join(", ", urlsToProcess));
            }

            _logger.LogJobInformation(_jobInfo, "Enqueued {Count} total query processing jobs.", allEnqueuedJobIds.Count);
            return allEnqueuedJobIds;
        }

        private void PopulateProcessingLookups(string resourceType, IReadOnlyCollection<(string Url, SearchParameterStatus Status)> urlStatuses, IReadOnlyList<long> jobIds)
        {
            if (!_transientResourceTypeJobs.TryGetValue(resourceType, out var jobs))
            {
                _transientResourceTypeJobs.Add(resourceType, (new HashSet<long>(jobIds), new Counts()));
            }
            else
            {
                foreach (var jobId in jobIds)
                {
                    jobs.JobIds.Add(jobId);
                }
            }

            foreach (var jobId in jobIds)
            {
                _transientProcessingJobIds.Add(jobId);
            }

            foreach (var urlStatus in urlStatuses)
            {
                if (!_transientSearchParamResouceTypes.TryGetValue(urlStatus.Url, out var lookup))
                {
                    _transientSearchParamResouceTypes.Add(urlStatus.Url, (new HashSet<string>([resourceType]), urlStatus.Status));
                }
                else
                {
                    lookup.ResourceTypes.Add(resourceType);
                }
            }
        }

        private async Task<IReadOnlyList<long>> CreateAndEnqueueJobDefinitionsAsync(
            IReadOnlyList<(long StartId, long EndId, int Count)> ranges,
            string resourceType,
            List<(string Url, SearchParameterStatus Status)> searchParamUrlStatuses,
            CancellationToken cancellationToken)
        {
            var definitions = new List<string>();

            foreach (var range in ranges)
            {
                var reindexJobPayload = new ReindexProcessingJobDefinition()
                {
                    SearchParamLastUpdated = _searchParamLastUpdated,
                    TypeId = (int)JobType.ReindexProcessing,
                    GroupId = _jobInfo.GroupId,
                    SearchParameterHash = GetSearchParameterHash(resourceType),
                    ResourceCount = new SearchResultReindex
                    {
                        StartResourceSurrogateId = range.StartId,
                        EndResourceSurrogateId = range.EndId,
                        Count = range.Count, ////countOnlyResults?.TotalCount ?? 0,
                    },
                    ResourceType = resourceType,
                    MaximumNumberOfResourcesPerQuery = _definition.MaximumNumberOfResourcesPerQuery,
                    MaximumNumberOfResourcesPerWrite = _definition.MaximumNumberOfResourcesPerWrite,
                    SearchParameterUrlStatuses = searchParamUrlStatuses.ToImmutableList(),
                };

                definitions.Add(JsonConvert.SerializeObject(reindexJobPayload));
            }

            if (definitions.Count == 0)
            {
                return new List<long>();
            }

            try
            {
                var jobIds = await _timeoutRetries.ExecuteAsync(
                    async () => (await _queueClient.EnqueueAsync((byte)QueueType.Reindex, definitions.ToArray(), _jobInfo.GroupId, false, cancellationToken)).Select(job => job.Id).ToList());
                _logger.LogJobInformation(_jobInfo, "Enqueued batch of {Count} jobs for resource type {ResourceType}.", jobIds.Count, resourceType);
                return jobIds;
            }
            catch (Exception ex)
            {
                _logger.LogJobError(ex, _jobInfo, "Failed to enqueue jobs for resource type {ResourceType}.", resourceType);
                throw;
            }
        }

        /// <summary>
        /// This is the starting point for how many resources per resource type are found
        /// No change to these ResourceCounts occurs after this initial setup
        /// We also store the total # of resources to be reindexed
        /// </summary>
        /// <returns>Task</returns>
        private async Task CalculateAndSetTotalAndResourceCounts(HashSet<string> resourceTypes)
        {
            foreach (string resourceType in resourceTypes)
            {
                var queryForCount = new ReindexJobQueryStatus(resourceType, continuationToken: null)
                {
                    LastModified = Clock.UtcNow,
                    Status = OperationStatus.Queued,
                };

                SearchResult searchResult = await GetResourceCountForQueryAsync(queryForCount, countOnly: true, true, _cancellationToken);
                if (searchResult?.ReindexResult?.StartResourceSurrogateId > 0)
                {
                    SearchResultReindex reindexResults = searchResult.ReindexResult;
                    _resourceCounts.TryAdd(resourceType, new SearchResultReindex()
                    {
                        Count = reindexResults.Count,
                        EndResourceSurrogateId = reindexResults.EndResourceSurrogateId,
                        StartResourceSurrogateId = reindexResults.StartResourceSurrogateId,
                    });
                }
                else if (searchResult?.TotalCount != null && searchResult.TotalCount.Value > 0)
                {
                    // No action needs to be taken if an entry for this resource fails to get added to the dictionary
                    // We will reindex all resource types that do not have a dictionary entry
                    _resourceCounts.TryAdd(resourceType, new SearchResultReindex(searchResult.TotalCount.Value));
                }
                else
                {
                    // no resources found, so this becomes a no-op entry just to show we did look it up but found no resources
                    _resourceCounts.TryAdd(resourceType, new SearchResultReindex(0));
                }
            }
        }

        private async Task<SearchResult> GetResourceCountForQueryAsync(ReindexJobQueryStatus queryStatus, bool countOnly, bool ignoreSearchParamHash, CancellationToken cancellationToken)
        {
            _resourceCounts.TryGetValue(queryStatus.ResourceType, out var searchResultReindex);
            var queryParametersList = new List<Tuple<string, string>>()
            {
                Tuple.Create(KnownQueryParameterNames.Count, _definition.MaximumNumberOfResourcesPerQuery.ToString()),
                Tuple.Create(KnownQueryParameterNames.Type, queryStatus.ResourceType),
            };

            // This should never be cosmos
            if (searchResultReindex != null)
            {
                // Use 'queryStatus.StartResourceSurrogateId' for the start of the range, unless it is ZERO: in that case use 'searchResultReindex.StartResourceSurrogateId'.
                // The same applies to 'queryStatus.EndResourceSurrogateId' as the end of the range, unless it is ZERO: in that case use 'searchResultReindex.EndResourceSurrogateId'.
                // The results of the SQL query will determine how many resources to actually return based on the configured maximumNumberOfResourcesPerQuery.
                // When this function returns, it knows what the next starting value to use in searching for the next block of results and will use that as the queryStatus starting point

                var startId = queryStatus.StartResourceSurrogateId > 0 ? queryStatus.StartResourceSurrogateId.ToString() : searchResultReindex.StartResourceSurrogateId.ToString();
                var endId = queryStatus.EndResourceSurrogateId > 0 ? queryStatus.EndResourceSurrogateId.ToString() : searchResultReindex.EndResourceSurrogateId.ToString();

                queryParametersList.AddRange(new[]
                {
                    Tuple.Create(KnownQueryParameterNames.EndSurrogateId, endId),
                    Tuple.Create(KnownQueryParameterNames.StartSurrogateId, startId),
                });
            }

            if (queryStatus.ContinuationToken != null)
            {
                queryParametersList.Add(Tuple.Create(KnownQueryParameterNames.ContinuationToken, queryStatus.ContinuationToken));
            }

            string searchParameterHash = string.Empty;
            searchParameterHash = GetSearchParameterHash(queryStatus.ResourceType);

            // Ensure searchParameterHash is never null - for Cosmos DB scenarios, this will be empty string
            searchParameterHash ??= string.Empty;

            if (ignoreSearchParamHash)
            {
                queryParametersList.Add(Tuple.Create(KnownQueryParameterNames.IgnoreSearchParamHash, "true"));
            }

            using (IScoped<ISearchService> searchService = _searchServiceFactory())
            {
                try
                {
                    return await _retries.ExecuteAsync(
                        async () => await searchService.Value.SearchForReindexAsync(queryParametersList, searchParameterHash, countOnly: countOnly, cancellationToken, true));
                }
                catch (Exception ex)
                {
                    var message = $"Error running reindex query for resource type {queryStatus.ResourceType}.";
                    var reindexJobException = new ReindexJobException(message, ex);
                    _logger.LogJobError(ex, _jobInfo, "Error running SearchForReindexAsync for resource type {ResourceType}.", queryStatus.ResourceType);
                    queryStatus.Error = reindexJobException.Message + " : " + ex.Message;

                    throw reindexJobException;
                }
            }
        }

        private async Task UpdateSearchParameterStatus(List<string> readySearchParameters, CancellationToken cancellationToken)
        {
            foreach (var searchParameterUrl in readySearchParameters.Where(_ => !_processedSearchParameters.Contains(_)))
            {
                var spStatus = _transientSearchParamResouceTypes[searchParameterUrl].Status;
                var output = spStatus == SearchParameterStatus.PendingDisable
                                ? SearchParameterStatus.Disabled
                                : spStatus == SearchParameterStatus.PendingDelete || spStatus == SearchParameterStatus.PendingHardDelete
                                    ? SearchParameterStatus.Deleted
                                    : spStatus == SearchParameterStatus.Supported || spStatus == SearchParameterStatus.Enabled
                                        ? SearchParameterStatus.Enabled
                                        : throw new InvalidOperationException("Unexpected input status");
                _logger.LogJobInformation(_jobInfo, "Reindex job updating the status of the fully indexed search parameter, parameter: '{ParamUri}' to {Status}.", searchParameterUrl, output);

                if (output == SearchParameterStatus.Deleted)
                {
                    await _retries.ExecuteAsync(
                        async () => await _searchParameterOperations.DeleteSearchParameterResourceAsync(searchParameterUrl, spStatus == SearchParameterStatus.PendingHardDelete, cancellationToken));
                }

                await _retries.ExecuteAsync(
                    async () => await _searchParameterStatusManager.UpdateSearchParameterStatusAsync(new List<string>() { searchParameterUrl }, output, cancellationToken, reindexId: _jobInfo.Id));
                _processedSearchParameters.Add(searchParameterUrl);
            }
        }

        private string GetSearchParameterHash(string resourceType)
        {
            _searchParameterDefinitionManager.SearchParameterHashMap.TryGetValue(resourceType, out string hash);
            return hash;
        }

        private async Task CheckForCompletionAsync(CancellationToken cancellationToken)
        {
            do
            {
                await Task.Delay(TimeSpan.FromSeconds(_operationsConfiguration.Reindex.JobsPollingIntervalSec), cancellationToken);

                var batch = _transientProcessingJobIds.Any()
                          ? await _timeoutRetries.ExecuteAsync(async () =>
                                await _queueClient.GetJobsByIdsAsync((byte)QueueType.Reindex, _transientProcessingJobIds.Take(_operationsConfiguration.Reindex.JobsBatchSize).ToArray(), true, cancellationToken))
                          : new List<JobInfo>();

                var finishedJobs = batch.Where(j => j.Status == JobStatus.Completed || j.Status == JobStatus.Failed).ToList();

                await ProcessFinishedJobs(finishedJobs, cancellationToken);
            }
            while (_transientProcessingJobIds.Any());
        }

        private async Task ProcessFinishedJobs(IReadOnlyList<JobInfo> finishedJobs, CancellationToken cancellationToken)
        {
            // remove processed jobs from _transientResourceTypeJobs and update counts
            foreach (var job in finishedJobs)
            {
                foreach (var resourceTypeJobs in _transientResourceTypeJobs.Where(_ => _.Value.JobIds.Count > 0))
                {
                    if (!resourceTypeJobs.Value.JobIds.Remove(job.Id))
                    {
                        continue;
                    }

                    //// if job failed it might not be able to set counts correctly, ignore data in result and set failed to all input and succeeded to 0
                    if (job.Status == JobStatus.Completed)
                    {
                        var result = JsonConvert.DeserializeObject<ReindexProcessingJobResult>(job.Result);
                        _result.SucceededResources += result.SucceededResourceCount;
                        _result.FailedResources += result.FailedResourceCount;
                        resourceTypeJobs.Value.Counts.Succeeded += result.SucceededResourceCount; // TODO: Do we need this?
                        resourceTypeJobs.Value.Counts.Failed += result.FailedResourceCount; // TODO: Do we need this?
                    }
                    else
                    {
                        var result = string.IsNullOrEmpty(job.Result) ? null : JsonConvert.DeserializeObject<ReindexProcessingJobResult>(job.Result);
                        if (result == null)
                        {
                            _logger.LogJobWarning(_jobInfo, "Processing job {ProcessingJobId} had an empty result payload. Status={Status}.", job.Id, job.Status);
                            AddErrorResult(OperationOutcomeConstants.IssueSeverity.Error, OperationOutcomeConstants.IssueType.Exception, $"Processing job {job.Id} failed but returned no result payload.");
                        }
                        else
                        {
                            var def = JsonConvert.DeserializeObject<ReindexProcessingJobDefinition>(job.Definition);
                            _result.FailedResources += def.ResourceCount.Count;
                            resourceTypeJobs.Value.Counts.Failed += def.ResourceCount.Count; // TODO: Do we need this?
                            AddErrorResult(OperationOutcomeConstants.IssueSeverity.Error, OperationOutcomeConstants.IssueType.Exception, $"Processing job failed for resource type {def.ResourceType}: {result.Error}");
                        }
                    }
                }

                _transientProcessingJobIds.Remove(job.Id);
            }

            _result.CompletedJobs += finishedJobs.Count(j => j.Status == JobStatus.Completed);

            // remove processed resource types from _transientSearchParamResouceTypes
            foreach (var completedResourceType in _transientResourceTypeJobs.Where(_ => _.Value.JobIds.Count == 0 && _.Value.Counts.Failed == 0).Select(_ => _.Key))
            {
                foreach (var searchParamResourceType in _transientSearchParamResouceTypes.Values)
                {
                    searchParamResourceType.ResourceTypes.Remove(completedResourceType);
                }
            }

            // deal with completed search params
            var completedSearchParams = _transientSearchParamResouceTypes.Where(_ => _.Value.ResourceTypes.Count == 0).Select(_ => _.Key).ToList();
            if (completedSearchParams.Any())
            {
                await UpdateSearchParameterStatus(completedSearchParams, cancellationToken);
            }

            // update counts when all done
            var allJobsComplete = _transientResourceTypeJobs.Values.All(_ => _.JobIds.Count == 0);
            if (allJobsComplete)
            {
                _jobInfo.Data = _result.SucceededResources + _result.FailedResources;
                _logger.LogInformation("Finished processing jobs for Group Id: {Id}. Total completed: {CompletedCount} out of {CreatedCount}", _jobInfo.GroupId, _result.CompletedJobs, _result.CreatedJobs);
            }
        }

        private async Task TryLogEvent(string process, string status, string text, DateTime? startDate, CancellationToken cancellationToken)
        {
            await _searchParameterStatusManager.TryLogEvent(process, status, text, startDate, cancellationToken);
        }

        private class Counts
        {
            public long Succeeded { get; set; }

            public long Failed { get; set; }
        }
    }
}
