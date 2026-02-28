// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.Model;
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
        private readonly CoreFeatureConfiguration _coreFeatureConfiguration;
        private readonly OperationsConfiguration _operationsConfiguration;

        private CancellationToken _cancellationToken;
        private IQueueClient _queueClient;
        private JobInfo _jobInfo;
        private ReindexJobRecord _reindexJobRecord;
        private ReindexOrchestratorJobResult _currentResult;
        private IReadOnlyCollection<ResourceSearchParameterStatus> _initialSearchParamStatusCollection;
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
        private static readonly AsyncPolicy _searchParameterStatusRetries = Policy.WrapAsync(_requestRateRetries, _timeoutRetries);

        private readonly HashSet<string> _processedSearchParameters = new HashSet<string>(); // to prevent multiple status updates
        private readonly HashSet<long> _processedJobIds = new HashSet<long>(); // to look at a completed job only once

        // Transient dictionaries below are populated on processing job creates. After a job is in the terminal state
        // it is removed from _transientResourceTypeJobs. When all jobs removed then resource type is completed.
        // Similar concept is used for _transientSearchParamResouceTypes
        private readonly Dictionary<string, (HashSet<long> JobIds, Counts Counts)> _transientResourceTypeJobs = new Dictionary<string, (HashSet<long> JobIds, Counts Counts)>();
        private readonly Dictionary<string, HashSet<string>> _transientSearchParamResouceTypes = new Dictionary<string, HashSet<string>>();

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
            _coreFeatureConfiguration = coreFeatureConfiguration.Value;
            _operationsConfiguration = operationsConfiguration.Value;

            // Determine support for surrogate ID ranging once
            // This is to ensure Gen1 Reindex still works as expected but we still maintain perf on job inseration to SQL
            _isSurrogateIdRangingSupported = fhirRuntimeConfiguration.IsSurrogateIdRangingSupported;
            _logger.LogInformation(_isSurrogateIdRangingSupported ? "Using SQL Server search service with surrogate ID ranging support" : "Using search service without surrogate ID ranging support (likely Cosmos DB)");
        }

        public async Task<string> ExecuteAsync(JobInfo jobInfo, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(jobInfo, nameof(jobInfo));
            _currentResult = string.IsNullOrEmpty(jobInfo.Result) ? new ReindexOrchestratorJobResult() : JsonConvert.DeserializeObject<ReindexOrchestratorJobResult>(jobInfo.Result);
            var reindexJobRecord = JsonConvert.DeserializeObject<ReindexJobRecord>(jobInfo.Definition);
            _jobInfo = jobInfo;
            _reindexJobRecord = reindexJobRecord;
            _cancellationToken = cancellationToken; // TODO: Do we need cancel?

            try
            {
                await RefreshSearchParameterCache(true);

                _reindexJobRecord.Status = OperationStatus.Running;
                _jobInfo.Status = JobStatus.Running;
                _logger.LogInformation("Reindex job with Id: {Id} has been started. Status: {Status}.", _jobInfo.Id, _reindexJobRecord.Status);

                var jobs = await _queueClient.GetJobByGroupIdAsync((byte)QueueType.Reindex, _jobInfo.GroupId, true, cancellationToken);
                var processingJobs = jobs.Where(j => j.Id != _jobInfo.GroupId).ToList();

                // For SQL Server, always attempt job creation - we use Export-style resume logic
                // to calculate remaining work from existing jobs, preventing duplicates.
                // For Cosmos, use the existing binary check since job definitions don't have unique ranges.
                if (_isSurrogateIdRangingSupported || !processingJobs.Any())
                {
                    if (processingJobs.Any())
                    {
                        _logger.LogJobInformation(_jobInfo, "Found {Count} existing processing jobs. Re-submitting jobs (database handles deduplication).", processingJobs.Count);
                    }

                    await CreateReindexProcessingJobsAsync(cancellationToken);
                    jobs = await _queueClient.GetJobByGroupIdAsync((byte)QueueType.Reindex, _jobInfo.GroupId, true, cancellationToken);
                    processingJobs = jobs.Where(j => j.Id != _jobInfo.GroupId).ToList(); // TODO: Move this logic inside create
                }
                else // cosmos job restart
                {
                    foreach (var job in processingJobs.Select(_ => new { _.Id, Def = JsonConvert.DeserializeObject<ReindexProcessingJobDefinition>(_.Definition) }))
                    {
                        PopulateProcessingLookups(job.Def.ResourceType, job.Def.SearchParameterUrls, [job.Id]);
                    }
                }

                _currentResult.CreatedJobs = processingJobs.Count; // TODO: Move this logic inside create

                await CheckForCompletionAsync(processingJobs, cancellationToken);

                await RefreshSearchParameterCache(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogJobInformation(jobInfo, "The reindex job was cancelled by caller, Id: {Id}", jobInfo.Id);
                AddErrorResult(OperationOutcomeConstants.IssueSeverity.Information, OperationOutcomeConstants.IssueType.Informational, Core.Resources.ReindexingCancelledbyCaller);
            }
            catch (OperationCanceledException ex)
            {
                _logger.LogJobError(ex, _jobInfo, "The reindex job was canceled, Id: {Id}", _jobInfo.Id);
                AddErrorResult(OperationOutcomeConstants.IssueSeverity.Error, OperationOutcomeConstants.IssueType.Incomplete, Core.Resources.ReindexingJobCancelled);
            }
            catch (Exception ex)
            {
                AddErrorResult(OperationOutcomeConstants.IssueSeverity.Error, OperationOutcomeConstants.IssueType.Exception, ex.Message);
                LogReindexJobRecordErrorMessage();
                _logger.LogJobError(ex, _jobInfo, "ReindexJob Failed and didn't complete.");
            }

            return JsonConvert.SerializeObject(_currentResult);
        }

        private async Task WaitForRefresh()
        {
            await Task.Delay(_operationsConfiguration.Reindex.CacheRefreshWaitMultiplier * _coreFeatureConfiguration.SearchParameterCacheRefreshIntervalSeconds * 1000, _cancellationToken);
        }

        // before starting anything wait for natural cache refresh. this will also make sure that all processing pods have latest search param definitions.
        private async Task RefreshSearchParameterCache(bool isReindexStart)
        {
            var suffix = isReindexStart ? "Start" : "End";
            _logger.LogJobInformation(_jobInfo, $"Reindex orchestrator job started cache refresh at the {suffix}.");
            await TryLogEvent($"ReindexOrchestratorJob={_jobInfo.Id}.ExecuteAsync.{suffix}", "Warn", "Started", null, _cancellationToken);
            await WaitForRefresh(); // wait for M * cache refresh intervals

            // capture search param last updated to pass to processing jobs for comparison
            var currentDate = _searchParameterOperations.SearchParamLastUpdated.HasValue ? _searchParameterOperations.SearchParamLastUpdated.Value : DateTimeOffset.MinValue;
            _searchParamLastUpdated = currentDate;
            _logger.LogJobInformation(_jobInfo, $"Reindex orchestrator job completed cache refresh at the {suffix}: SearchParamLastUpdated {_searchParamLastUpdated}");
            await TryLogEvent($"ReindexOrchestratorJob={_jobInfo.Id}.ExecuteAsync.{suffix}", "Warn", $"SearchParamLastUpdated={_searchParamLastUpdated.ToString("yyyy-MM-dd HH:mm:ss.fff")}", null, _cancellationToken);
        }

        private async Task<IReadOnlyList<long>> CreateReindexProcessingJobsAsync(CancellationToken cancellationToken)
        {
            // Build queries based on new search params
            // Find search parameters not in a final state such as supported, pendingDelete, pendingDisable.
            List<SearchParameterStatus> validStatus = new List<SearchParameterStatus>() { SearchParameterStatus.Supported, SearchParameterStatus.PendingDelete, SearchParameterStatus.PendingDisable };
            _initialSearchParamStatusCollection = await _searchParameterStatusManager.GetAllSearchParameterStatus(cancellationToken);

            // Get all URIs that have at least one entry with a valid status
            // This handles case-variant duplicates naturally
            var validUris = _initialSearchParamStatusCollection
                .Where(s => validStatus.Contains(s.Status))
                .Select(s => s.Uri.ToString())
                .ToHashSet();

            // Filter to only those search parameters which have valid definitions
            var possibleNotYetIndexedParams = new List<SearchParameterInfo>();
            foreach (var validUri in validUris)
            {
                if (_searchParameterDefinitionManager.TryGetSearchParameter(validUri, out var searchInfo))
                {
                    possibleNotYetIndexedParams.Add(searchInfo);
                    var msg = $"status={searchInfo.SearchParameterStatus} uri={validUri}";
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

            var notYetIndexedParams = new List<SearchParameterInfo>();

            var resourceTypeList = new HashSet<string>();

            // filter list of SearchParameters by the target resource types
            if (_reindexJobRecord.TargetResourceTypes.Any())
            {
                foreach (var searchParam in possibleNotYetIndexedParams)
                {
                    var searchParamResourceTypes = GetDerivedResourceTypes(searchParam.BaseResourceTypes);
                    var matchingResourceTypes = searchParamResourceTypes.Intersect(_reindexJobRecord.TargetResourceTypes).ToList();
                    if (matchingResourceTypes.Any())
                    {
                        notYetIndexedParams.Add(searchParam);

                        // add matching resource types to the set of resource types which we will reindex
                        resourceTypeList.UnionWith(matchingResourceTypes);

                        foreach (var resourceType in matchingResourceTypes) // TODO: Find better place
                        {
                            PopulateProcessingLookups(resourceType, [searchParam.Url.OriginalString], new List<long>());
                        }
                    }
                    else
                    {
                        _logger.LogJobInformation(_jobInfo, "Search parameter {Url} is not being reindexed as it does not match the target types of reindex job Id: {Reindexid}.", searchParam.Url, _jobInfo.Id);
                    }
                }
            }
            else
            {
                notYetIndexedParams.AddRange(possibleNotYetIndexedParams);

                // From the param list, get the list of necessary resources which should be
                // included in our query
                foreach (var param in notYetIndexedParams)
                {
                    var searchParamResourceTypes = GetDerivedResourceTypes(param.BaseResourceTypes);
                    resourceTypeList.UnionWith(searchParamResourceTypes);
                    foreach (var resourceType in resourceTypeList) // TODO: Find better place
                    {
                        PopulateProcessingLookups(resourceType, [param.Url.OriginalString], new List<long>());
                    }
                }
            }

            // if there are not any parameters which are supported but not yet indexed, then we have nothing to do
            if (!notYetIndexedParams.Any() && resourceTypeList.Count == 0)
            {
                AddErrorResult(
                    OperationOutcomeConstants.IssueSeverity.Information,
                    OperationOutcomeConstants.IssueType.Informational,
                    string.Format(Core.Resources.ReindexingNoSearchParameterstoReindex, _jobInfo.Id));
                return new List<long>();
            }

            // Save the list of resource types in the reindexjob document
            foreach (var resourceType in resourceTypeList)
            {
                _reindexJobRecord.Resources.Add(resourceType);
            }

            // save the list of search parameters to the reindexjob document
            foreach (var url in notYetIndexedParams.Select(p => p.Url.OriginalString))
            {
                _reindexJobRecord.SearchParams.Add(url);
            }

            await CalculateAndSetTotalAndResourceCounts();

            // Handle search parameters for resource types with count 0
            var resourceTypesWithZeroCount = _reindexJobRecord.ResourceCounts
                .Where(kvp => kvp.Value.Count == 0)
                .Select(kvp => kvp.Key)
                .ToList();

            // Confirm counts for range by not ignoring hash this time incase it's 0
            // Because we ignore hash to get full range set for initial count, we need to double-check counts here
            foreach (var resourceCount in _reindexJobRecord.ResourceCounts)
            {
                var resourceType = resourceCount.Key;
                var resourceCountValue = resourceCount.Value;
                var startResourceSurrogateId = resourceCountValue.StartResourceSurrogateId;
                var endResourceSurrogateId = resourceCountValue.EndResourceSurrogateId;
                var count = resourceCountValue.Count;

                var queryForCount = new ReindexJobQueryStatus(resourceType, continuationToken: null)
                {
                    LastModified = Clock.UtcNow,
                    Status = OperationStatus.Queued,
                    StartResourceSurrogateId = startResourceSurrogateId,
                    EndResourceSurrogateId = endResourceSurrogateId,
                };

                SearchResult countOnlyResults = await GetResourceCountForQueryAsync(queryForCount, countOnly: true, false, _cancellationToken);

                // Check if the result has no records and add to zero-count list
                if (countOnlyResults?.TotalCount == 0)
                {
                    if (!resourceTypesWithZeroCount.Contains(resourceType))
                    {
                        resourceTypesWithZeroCount.Add(resourceType);

                        // subtract this count from JobRecordCount
                        _reindexJobRecord.Count -= resourceCountValue.Count;

                        // Update the ResourceCounts entry to reflect zero count
                        resourceCountValue.Count = 0;
                    }
                }
            }

            // Generate separate queries for each resource type and add them to query list.
            // This is the starting point for what essentially kicks off the reindexing job
            foreach (KeyValuePair<string, SearchResultReindex> resourceType in _reindexJobRecord.ResourceCounts.Where(e => e.Value.Count > 0))
            {
                var query = new ReindexJobQueryStatus(resourceType.Key, continuationToken: null)
                {
                    LastModified = Clock.UtcNow,
                    Status = OperationStatus.Queued,
                    StartResourceSurrogateId = GetSearchResultReindex(resourceType.Key).StartResourceSurrogateId,
                };

                _reindexJobRecord.QueryList.TryAdd(query, 1);
            }

            return await EnqueueQueryProcessingJobsAsync(cancellationToken);
        }

        private void AddErrorResult(string severity, string issueType, string message)
        {
            var errorList = new List<OperationOutcomeIssue>
            {
                new OperationOutcomeIssue(
                    severity,
                    issueType,
                    message),
            };
            errorList.AddRange(_currentResult.Error);
            _currentResult.Error = errorList;
        }

        private async Task<IReadOnlyList<long>> EnqueueQueryProcessingJobsAsync(CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException("Reindex operation cancelled by customer.");
            }

            var resourcesPerJob = (int)_reindexJobRecord.MaximumNumberOfResourcesPerQuery;
            var allEnqueuedJobIds = new List<long>();

            foreach (var resourceTypeEntry in _reindexJobRecord.ResourceCounts.Where(e => e.Value.Count > 0))
            {
                var resourceType = resourceTypeEntry.Key;
                var resourceCount = resourceTypeEntry.Value;

                // Get search parameters that are valid for this specific resource type
                var validSearchParameterUrls = GetValidSearchParameterUrlsForResourceType(resourceType);

                if (!validSearchParameterUrls.Any())
                {
                    _logger.LogJobWarning(_jobInfo, "No valid search parameters found for resource type {ResourceType} in reindex job {JobId}.", resourceType, _jobInfo.Id);
                }

                PopulateProcessingLookups(resourceType, validSearchParameterUrls, new List<long>());

                int totalRangesEnqueued = 0;

                // Check if surrogate ID ranging hasn't been determined yet or is supported
                if (_isSurrogateIdRangingSupported)
                {
                    // Use batched calls to GetSurrogateIdRanges to avoid timeout on large tables
                    // Following the same pattern as Export job
                    // Stream and enqueue each batch immediately so workers can start processing sooner
                    var numberOfRangesPerBatch = _operationsConfiguration.Reindex.NumberOfRecordRanges;
                    long startId = resourceCount.StartResourceSurrogateId;
                    long endId = resourceCount.EndResourceSurrogateId;

                    _logger.LogJobInformation(
                        _jobInfo,
                        "Fetching and enqueueing surrogate ID ranges for resource type {ResourceType} in batches of {BatchSize}. StartId={StartId}, EndId={EndId}",
                        resourceType,
                        numberOfRangesPerBatch,
                        startId,
                        endId);

                    using (IScoped<ISearchService> searchService = _searchServiceFactory())
                    {
                        IReadOnlyList<(long StartId, long EndId, int Count)> ranges;
                        do
                        {
                            // Check for cancellation between batches
                            if (cancellationToken.IsCancellationRequested)
                            {
                                throw new OperationCanceledException("Reindex operation cancelled by customer.");
                            }

                            ranges = await searchService.Value.GetSurrogateIdRanges(
                                resourceType,
                                startId,
                                endId,
                                resourcesPerJob,
                                numberOfRangesPerBatch,
                                true,
                                cancellationToken,
                                true);

                            if (ranges.Any())
                            {
                                // Stream: Create and enqueue job definitions for this batch immediately
                                var batchJobIds = await CreateAndEnqueueJobDefinitionsAsync(
                                    ranges,
                                    resourceType,
                                    validSearchParameterUrls,
                                    cancellationToken);

                                PopulateProcessingLookups(resourceType, validSearchParameterUrls, batchJobIds);

                                allEnqueuedJobIds.AddRange(batchJobIds);
                                totalRangesEnqueued += ranges.Count;

                                startId = ranges[^1].EndId + 1; // Move past the last range
                            }
                        }
                        while (ranges.Any());
                    }

                    _logger.LogJobInformation(
                        _jobInfo,
                        "Completed fetching and enqueueing {RangeCount} surrogate ID ranges for resource type {ResourceType}.",
                        totalRangesEnqueued,
                        resourceType);
                }
                else
                {
                    // Traditional chunking approach for Cosmos or fallback
                    long totalResources = resourceCount.Count;

                    int numberOfChunks = (int)Math.Ceiling(totalResources / (double)resourcesPerJob);
                    _logger.LogJobInformation(_jobInfo, "Using calculated ranges for resource type {ResourceType}. Creating {Count} chunks.", resourceType, numberOfChunks);

                    // Create uniform-sized chunks based on resource count
                    var processingRanges = new List<(long StartId, long EndId, int Count)>();
                    for (int i = 0; i < numberOfChunks; i++)
                    {
                        processingRanges.Add((0, 0, 0)); // For Cosmos, we don't use surrogate IDs directly
                    }

                    // Enqueue all Cosmos ranges at once (they don't have the same large-scale issue)
                    var batchJobIds = await CreateAndEnqueueJobDefinitionsAsync(
                        processingRanges,
                        resourceType,
                        validSearchParameterUrls,
                        cancellationToken);

                    PopulateProcessingLookups(resourceType, validSearchParameterUrls, batchJobIds);

                    allEnqueuedJobIds.AddRange(batchJobIds);
                }

                _logger.LogJobInformation(
                    _jobInfo,
                    "Created jobs for resource type {ResourceType} with {Count} valid search parameters: {SearchParams}",
                    resourceType,
                    validSearchParameterUrls.Count,
                    string.Join(", ", validSearchParameterUrls));
            }

            _logger.LogJobInformation(_jobInfo, "Enqueued {Count} total query processing jobs.", allEnqueuedJobIds.Count);
            return allEnqueuedJobIds;
        }

        private void PopulateProcessingLookups(string resourceType, IReadOnlyCollection<string> urls, IReadOnlyList<long> jobIds)
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

            foreach (var url in urls)
            {
                if (!_transientSearchParamResouceTypes.TryGetValue(url, out var resourceTypes))
                {
                    _transientSearchParamResouceTypes.Add(url, new HashSet<string>([resourceType]));
                }
                else
                {
                    resourceTypes.Add(resourceType);
                }
            }
        }

        /// <summary>
        /// Creates job definitions from ranges and enqueues them immediately.
        /// This enables streaming/pipelining where workers can start processing while more ranges are being fetched.
        /// </summary>
        private async Task<IReadOnlyList<long>> CreateAndEnqueueJobDefinitionsAsync(
            IReadOnlyList<(long StartId, long EndId, int Count)> ranges,
            string resourceType,
            List<string> validSearchParameterUrls,
            CancellationToken cancellationToken)
        {
            var definitions = new List<string>();

            foreach (var range in ranges)
            {
                var queryForCount = new ReindexJobQueryStatus(resourceType, continuationToken: null)
                {
                    LastModified = Clock.UtcNow,
                    Status = OperationStatus.Queued,
                    StartResourceSurrogateId = range.StartId,
                    EndResourceSurrogateId = range.EndId,
                };

                SearchResult countOnlyResults = await GetResourceCountForQueryAsync(queryForCount, countOnly: true, false, cancellationToken);

                if (countOnlyResults?.TotalCount == 0)
                {
                    // nothing to do here
                    continue;
                }

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
                        Count = countOnlyResults?.TotalCount ?? 0,
                    },
                    ResourceType = resourceType,
                    MaximumNumberOfResourcesPerQuery = _reindexJobRecord.MaximumNumberOfResourcesPerQuery,
                    MaximumNumberOfResourcesPerWrite = _reindexJobRecord.MaximumNumberOfResourcesPerWrite,
                    SearchParameterUrls = validSearchParameterUrls.ToImmutableList(),
                };

                definitions.Add(JsonConvert.SerializeObject(reindexJobPayload));
            }

            if (!definitions.Any())
            {
                return Array.Empty<long>();
            }

            try
            {
                var jobIds = await _timeoutRetries.ExecuteAsync(async () => (await _queueClient.EnqueueAsync((byte)QueueType.Reindex, definitions.ToArray(), _jobInfo.GroupId, false, cancellationToken))
                    .Select(job => job.Id)
                    .OrderBy(id => id)
                    .ToList());

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
        private async Task CalculateAndSetTotalAndResourceCounts()
        {
            long totalCount = 0;
            foreach (string resourceType in _reindexJobRecord.Resources)
            {
                var queryForCount = new ReindexJobQueryStatus(resourceType, continuationToken: null)
                {
                    LastModified = Clock.UtcNow,
                    Status = OperationStatus.Queued,
                };

                SearchResult searchResult = await GetResourceCountForQueryAsync(queryForCount, countOnly: true, true, _cancellationToken);
                totalCount += searchResult != null ? searchResult.TotalCount.Value : 0;

                if (searchResult?.ReindexResult?.StartResourceSurrogateId > 0)
                {
                    SearchResultReindex reindexResults = searchResult.ReindexResult;
                    _reindexJobRecord.ResourceCounts.TryAdd(resourceType, new SearchResultReindex()
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
                    _reindexJobRecord.ResourceCounts.TryAdd(resourceType, new SearchResultReindex(searchResult.TotalCount.Value));
                }
                else
                {
                    // no resources found, so this becomes a no-op entry just to show we did look it up but found no resources
                    _reindexJobRecord.ResourceCounts.TryAdd(resourceType, new SearchResultReindex(0));
                }
            }

            if (_reindexJobRecord.Count == 0)
            {
                _reindexJobRecord.Count = totalCount;
            }
        }

        private async Task<SearchResult> GetResourceCountForQueryAsync(ReindexJobQueryStatus queryStatus, bool countOnly, bool ignoreSearchParamHash, CancellationToken cancellationToken)
        {
            SearchResultReindex searchResultReindex = GetSearchResultReindex(queryStatus.ResourceType);
            var queryParametersList = new List<Tuple<string, string>>()
            {
                Tuple.Create(KnownQueryParameterNames.Count, _reindexJobRecord.MaximumNumberOfResourcesPerQuery.ToString()),
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
                    Tuple.Create(KnownQueryParameterNames.GlobalEndSurrogateId, "0"),
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
                    return await searchService.Value.SearchForReindexAsync(queryParametersList, searchParameterHash, countOnly: countOnly, cancellationToken, true);
                }
                catch (Exception ex)
                {
                    var message = $"Error running reindex query for resource type {queryStatus.ResourceType}.";
                    var reindexJobException = new ReindexJobException(message, ex);
                    _logger.LogJobError(ex, _jobInfo, "Error running SearchForReindexAsync for resource type {ResourceType}.", queryStatus.ResourceType);
                    queryStatus.Error = reindexJobException.Message + " : " + ex.Message;
                    LogReindexJobRecordErrorMessage();

                    throw reindexJobException;
                }
            }
        }

        private async Task UpdateSearchParameterStatus(List<string> readySearchParameters, CancellationToken cancellationToken)
        {
            var fullyIndexedParamUris = new List<string>();
            var searchParamStatusCollection = await _searchParameterStatusManager.GetAllSearchParameterStatus(cancellationToken);

            foreach (var searchParameterUrl in readySearchParameters.Where(_ => !_processedSearchParameters.Contains(_)))
            {
                var spStatus = searchParamStatusCollection.FirstOrDefault(sp => string.Equals(sp.Uri.OriginalString, searchParameterUrl, StringComparison.Ordinal))?.Status;

                switch (spStatus)
                {
                    case SearchParameterStatus.PendingDisable:
                        _logger.LogJobInformation(_jobInfo, "Reindex job updating the status of the fully indexed search parameter, parameter: '{ParamUri}' to Disabled.", searchParameterUrl);
                        await _searchParameterStatusRetries.ExecuteAsync(
                            async () => await _searchParameterStatusManager.UpdateSearchParameterStatusAsync([searchParameterUrl], SearchParameterStatus.Disabled, cancellationToken));
                        _processedSearchParameters.Add(searchParameterUrl);
                        break;
                    case SearchParameterStatus.PendingDelete:
                        _logger.LogJobInformation(_jobInfo, "Reindex job updating the status of the fully indexed search parameter, parameter: '{ParamUri}' to Deleted.", searchParameterUrl);
                        await _searchParameterStatusRetries.ExecuteAsync(
                            async () => await _searchParameterStatusManager.UpdateSearchParameterStatusAsync([searchParameterUrl], SearchParameterStatus.Deleted, cancellationToken));
                        _processedSearchParameters.Add(searchParameterUrl);
                        break;
                    case SearchParameterStatus.Supported:
                    case SearchParameterStatus.Enabled:
                        fullyIndexedParamUris.Add(searchParameterUrl);
                        break;
                }
            }

            if (fullyIndexedParamUris.Count > 0)
            {
                _logger.LogJobInformation(_jobInfo, "Reindex job updating the status of the fully indexed search parameter, parameters: '{ParamUris} to Enabled.'", string.Join("', '", fullyIndexedParamUris));
                await _searchParameterStatusRetries.ExecuteAsync(
                    async () => await _searchParameterStatusManager.UpdateSearchParameterStatusAsync(fullyIndexedParamUris, SearchParameterStatus.Enabled, _cancellationToken));
                _processedSearchParameters.UnionWith(fullyIndexedParamUris);
            }
        }

        private HashSet<string> GetDerivedResourceTypes(IReadOnlyCollection<string> resourceTypes)
        {
            var completeResourceList = new HashSet<string>(resourceTypes);

            foreach (var baseResourceType in resourceTypes)
            {
                if (baseResourceType == KnownResourceTypes.Resource)
                {
                    completeResourceList.UnionWith(_modelInfoProvider.GetResourceTypeNames().ToHashSet());

                    // We added all possible resource types, so no need to continue
                    break;
                }

                if (baseResourceType == KnownResourceTypes.DomainResource)
                {
                    var domainResourceChildResourceTypes = _modelInfoProvider.GetResourceTypeNames().ToHashSet();

                    // Remove types that inherit from Resource directly
                    domainResourceChildResourceTypes.Remove(KnownResourceTypes.Binary);
                    domainResourceChildResourceTypes.Remove(KnownResourceTypes.Bundle);
                    domainResourceChildResourceTypes.Remove(KnownResourceTypes.Parameters);

                    completeResourceList.UnionWith(domainResourceChildResourceTypes);
                }
            }

            return completeResourceList;
        }

        private SearchResultReindex GetSearchResultReindex(string resourceType)
        {
            _reindexJobRecord.ResourceCounts.TryGetValue(resourceType, out SearchResultReindex searchResultReindex);
            return searchResultReindex;
        }

        private string GetSearchParameterHash(string resourceType)
        {
            _searchParameterDefinitionManager.SearchParameterHashMap.TryGetValue(resourceType, out string hash);
            return hash;
        }

        private void LogReindexJobRecordErrorMessage()
        {
            _reindexJobRecord.Status = OperationStatus.Failed;
            _jobInfo.Status = JobStatus.Failed;

            var ser = JsonConvert.SerializeObject(_reindexJobRecord);
            _logger.LogJobInformation(_jobInfo, "ReindexJob Error: Current ReindexJobRecord for reference: {ReindexJobRecord}", ser);
        }

        private async Task CheckForCompletionAsync(List<JobInfo> processingJobs, CancellationToken cancellationToken)
        {
            if (processingJobs.Count == 0)
            {
                await ProcessFinishedJobs(processingJobs, cancellationToken);
                return;
            }

            var activeJobs = processingJobs.Where(j => j.Status == JobStatus.Running || j.Status == JobStatus.Created).ToList();

            // Track job counts and timing
            int lastActiveJobCount = activeJobs.Count;
            int unchangedCount = 0;
            int changeDetectedCount = 0;

            const int MAX_UNCHANGED_CYCLES = 3;
            const int MIN_POLL_INTERVAL_MS = 10000;
            const int MAX_POLL_INTERVAL_MS = 30000;
            const int DEFAULT_POLL_INTERVAL_MS = 10000;

            int currentPollInterval = DEFAULT_POLL_INTERVAL_MS;

            do
            {
                try
                {
                    // Adjust polling interval based on activity
                    if (activeJobs.Count != lastActiveJobCount)
                    {
                        // Changes detected - increase polling frequency
                        changeDetectedCount++;
                        unchangedCount = 0;
                        currentPollInterval = Math.Max(MIN_POLL_INTERVAL_MS, currentPollInterval / (1 + (changeDetectedCount / 2)));

                        _logger.LogJobInformation(
                            _jobInfo,
                            "Reindex processing jobs changed for Id: {Id}. {Count} jobs active. Polling interval: {Interval}ms",
                            _jobInfo.Id,
                            activeJobs.Count,
                            currentPollInterval);

                        lastActiveJobCount = activeJobs.Count;
                    }
                    else
                    {
                        // No changes - gradually back off
                        unchangedCount++;
                        changeDetectedCount = 0;
                        if (unchangedCount > MAX_UNCHANGED_CYCLES)
                        {
                            currentPollInterval = Math.Min(MAX_POLL_INTERVAL_MS, currentPollInterval * 2);
                        }
                    }

                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    linkedCts.CancelAfter(currentPollInterval);

                    try
                    {
                        var newjobs =
                            (await _queueClient.GetJobByGroupIdAsync((byte)QueueType.Reindex, _jobInfo.GroupId, true, linkedCts.Token))
                                .Where(j => j.Id != _jobInfo.GroupId && !_processedJobIds.Contains(j.Id)).ToList();
                        activeJobs = newjobs.Where(j => j.Status == JobStatus.Running || j.Status == JobStatus.Created).ToList();
                        var newFinishedJobs = newjobs.Where(j => j.Status == JobStatus.Completed || j.Status == JobStatus.Failed).ToList();
                        await ProcessFinishedJobs(newFinishedJobs, cancellationToken);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        // Poll interval timeout occurred - log and continue with next iteration
                        _logger.LogDebug("Poll interval timeout occurred for job {JobId}. Continuing with next iteration.", _jobInfo.Id);
                    }

                    await Task.Delay(Math.Min(MIN_POLL_INTERVAL_MS, currentPollInterval / 10), cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogJobWarning(ex, _jobInfo, "Error checking job status for Id: {Id}. Will retry with increased interval.", _jobInfo.Id);
                    currentPollInterval = Math.Min(MAX_POLL_INTERVAL_MS, currentPollInterval * 2);
                    await Task.Delay(currentPollInterval, cancellationToken);
                }
            }
            while (activeJobs.Any());
        }

        private async Task ProcessFinishedJobs(IReadOnlyList<JobInfo> finishedJobs, CancellationToken cancellationToken)
        {
            // remove processed jobs from _transientResourceTypeJobs and update counts
            foreach (var job in finishedJobs)
            {
                var record = JsonConvert.DeserializeObject<ReindexProcessingJobResult>(job.Result);
                foreach (var resourceTypeJobs in _transientResourceTypeJobs.Where(_ => _.Value.JobIds.Count > 0))
                {
                    resourceTypeJobs.Value.JobIds.Remove(job.Id);
                    //// if job failed it might not be able to set counts correctly
                    //// ignore data in result and set failed to all input and succeeded to 0
                    if (job.Status == JobStatus.Failed)
                    {
                        var def = JsonConvert.DeserializeObject<ReindexProcessingJobDefinition>(job.Definition);
                        _currentResult.FailedResources += def.ResourceCount.Count;
                        resourceTypeJobs.Value.Counts.Failed += def.ResourceCount.Count; // TODO: Do we need this?
                    }
                    else
                    {
                        _currentResult.SucceededResources += record.SucceededResourceCount;
                        _currentResult.FailedResources += record.FailedResourceCount;
                        resourceTypeJobs.Value.Counts.Succeeded += record.SucceededResourceCount; // TODO: Do we need this?
                        resourceTypeJobs.Value.Counts.Failed += record.FailedResourceCount; // TODO: Do we need this?
                    }
                }

                _processedJobIds.Add(job.Id);
            }

            _currentResult.CompletedJobs += finishedJobs.Count(j => j.Status == JobStatus.Completed);

            // remove processed resource types from _transientSearchParamResouceTypes
            foreach (var completedResourceType in _transientResourceTypeJobs.Where(_ => _.Value.JobIds.Count == 0 && _.Value.Counts.Failed == 0).Select(_ => _.Key))
            {
                foreach (var searchParamResourceType in _transientSearchParamResouceTypes)
                {
                    searchParamResourceType.Value.Remove(completedResourceType);
                }
            }

            // deal with completed search params
            var completedSearchParams = _transientSearchParamResouceTypes.Where(_ => _.Value.Count == 0).Select(_ => _.Key).ToList();
            if (completedSearchParams.Any())
            {
                await UpdateSearchParameterStatus(completedSearchParams, cancellationToken);
            }

            // update counts when all done
            var allJobsComplete = _transientResourceTypeJobs.Values.All(_ => _.JobIds.Count == 0);
            if (allJobsComplete)
            {
                _jobInfo.Data = _currentResult.SucceededResources + _currentResult.FailedResources;
                _reindexJobRecord.Count = _jobInfo.Data.Value;
                _logger.LogInformation("Finished processing jobs for Group Id: {Id}. Total completed: {CompletedCount} out of {CreatedCount}", _jobInfo.GroupId, _currentResult.CompletedJobs, _currentResult.CreatedJobs);
            }
        }

        /// <summary>
        /// Gets the search parameter URLs that are valid for the specified resource type.
        /// Filters the reindex job's search parameters to only include those that apply to the given resource type.
        /// </summary>
        /// <param name="resourceType">The resource type to filter search parameters for</param>
        /// <returns>A list of search parameter URLs that are valid for the resource type</returns>
        private List<string> GetValidSearchParameterUrlsForResourceType(string resourceType)
        {
            var validSearchParameterUrls = new List<string>();

            try
            {
                // Get all search parameters that are valid for this resource type
                var searchParametersForResourceType = _searchParameterDefinitionManager.GetSearchParameters(resourceType);
                var validSearchParameterUrlsSet = searchParametersForResourceType.Select(sp => sp.Url.OriginalString).ToHashSet();

                // Filter the reindex job's search parameters to only include those valid for this resource type
                foreach (var searchParamUrl in _reindexJobRecord.SearchParams)
                {
                    if (validSearchParameterUrlsSet.Contains(searchParamUrl))
                    {
                        validSearchParameterUrls.Add(searchParamUrl);
                    }
                    else
                    {
                        _logger.LogDebug("Search parameter {SearchParamUrl} is not valid for resource type {ResourceType} and will be excluded from processing job", searchParamUrl, resourceType);
                }
                }

                // Additional validation: Ensure search parameters from the reindex job actually apply to this resource type
                // by checking their BaseResourceTypes
                var finalValidUrls = new List<string>();
                foreach (var searchParamUrl in validSearchParameterUrls)
                {
                    try
                    {
                        var searchParamInfo = _searchParameterDefinitionManager.GetSearchParameter(searchParamUrl);
                        var applicableResourceTypes = GetDerivedResourceTypes(searchParamInfo.BaseResourceTypes);

                        if (applicableResourceTypes.Contains(resourceType))
                        {
                            finalValidUrls.Add(searchParamUrl);
                        }
                        else
                        {
                            _logger.LogDebug("Search parameter {SearchParamUrl} base resource types {BaseTypes} do not include {ResourceType}", searchParamUrl, string.Join(", ", searchParamInfo.BaseResourceTypes), resourceType);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogJobWarning(ex, _jobInfo, "Error validating search parameter {SearchParamUrl} for resource type {ResourceType}", searchParamUrl, resourceType);
                    }
                }

                return finalValidUrls;
            }
            catch (Exception ex)
            {
                _logger.LogJobWarning(ex, _jobInfo, "Error getting valid search parameters for resource type {ResourceType}. Using all search parameters as fallback.", resourceType);

                // Fallback to all search parameters if there's an error
                return _reindexJobRecord.SearchParams.ToList();
            }
        }

        private async Task TryLogEvent(string process, string status, string text, DateTime? startDate, CancellationToken cancellationToken)
        {
            using IScoped<ISearchService> search = _searchServiceFactory();
            await search.Value.TryLogEvent(process, status, text, startDate, cancellationToken);
        }

        private class Counts
        {
            public long Succeeded { get; set; }

            public long Failed { get; set; }
        }
    }
}
