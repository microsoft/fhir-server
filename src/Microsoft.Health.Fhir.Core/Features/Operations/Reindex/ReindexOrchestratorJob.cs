// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Core;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Operations.Reindex.Models;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Models;
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
        private CancellationToken _cancellationToken;
        private readonly ISearchParameterOperations _searchParameterOperations;
        private IQueueClient _queueClient;
        private JobInfo _jobInfo;
        private ReindexJobRecord _reindexJobRecord;
        private ReindexOrchestratorJobResult _currentResult;
        private bool? _isSurrogateIdRangingSupported = null;
        private static readonly AsyncPolicy _timeoutRetries = Policy
            .Handle<SqlException>(ex => ex.IsExecutionTimeout())
            .WaitAndRetryAsync(3, _ => TimeSpan.FromMilliseconds(RandomNumberGenerator.GetInt32(1000, 5000)));

        public ReindexOrchestratorJob(
            IQueueClient queueClient,
            Func<IScoped<ISearchService>> searchServiceFactory,
            ISearchParameterDefinitionManager searchParameterDefinitionManager,
            IModelInfoProvider modelInfoProvider,
            ISearchParameterStatusManager searchParameterStatusManager,
            ISearchParameterOperations searchParameterOperations,
            ILoggerFactory loggerFactory)
        {
            EnsureArg.IsNotNull(queueClient, nameof(queueClient));
            EnsureArg.IsNotNull(searchServiceFactory, nameof(searchServiceFactory));
            EnsureArg.IsNotNull(loggerFactory, nameof(loggerFactory));
            EnsureArg.IsNotNull(searchParameterDefinitionManager, nameof(searchParameterDefinitionManager));
            EnsureArg.IsNotNull(modelInfoProvider, nameof(modelInfoProvider));
            EnsureArg.IsNotNull(searchParameterStatusManager, nameof(searchParameterStatusManager));
            EnsureArg.IsNotNull(searchParameterOperations, nameof(searchParameterOperations));

            _queueClient = queueClient;
            _searchServiceFactory = searchServiceFactory;
            _logger = loggerFactory.CreateLogger<ReindexOrchestratorJob>();
            _searchParameterDefinitionManager = searchParameterDefinitionManager;
            _modelInfoProvider = modelInfoProvider;
            _searchParameterStatusManager = searchParameterStatusManager;
            _searchParameterOperations = searchParameterOperations;
        }

        public async Task<string> ExecuteAsync(JobInfo jobInfo, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(jobInfo, nameof(jobInfo));
            _currentResult = string.IsNullOrEmpty(jobInfo.Result) ? new ReindexOrchestratorJobResult() : JsonConvert.DeserializeObject<ReindexOrchestratorJobResult>(jobInfo.Result);
            var reindexJobRecord = JsonConvert.DeserializeObject<ReindexJobRecord>(jobInfo.Definition);
            _jobInfo = jobInfo;
            _reindexJobRecord = reindexJobRecord;
            _cancellationToken = cancellationToken;

            // Check for any changes to Search Parameters
            await SyncSearchParameterStatusupdates(cancellationToken);

            try
            {
                var jobIds = await CreateReindexProcessingJobsAsync(cancellationToken);
                if (jobIds == null || !jobIds.Any())
                {
                    // Nothing to process so we are done.
                    AddErrorResult(OperationOutcomeConstants.IssueSeverity.Information, OperationOutcomeConstants.IssueType.Informational, "Nothing to process. Reindex complete.");
                    return JsonConvert.SerializeObject(_currentResult);
                }

                // Instead of throwing RetriableJobException, set status and return
                _reindexJobRecord.Status = OperationStatus.Running;
                _jobInfo.Status = JobStatus.Running;
                _currentResult.CreatedJobs = jobIds.Count;
                _logger.LogInformation("Reindex job with Id: {Id} has been started. Status: {Status}.", _jobInfo.Id, OperationStatus.Running);

                // There should be ReindexProcessingJobs in there.
                var jobs = await _queueClient.GetJobByGroupIdAsync((byte)QueueType.Reindex, _jobInfo.GroupId, true, cancellationToken);
                var queryProcessingJobs = jobs.Where(j => j.Id != _jobInfo.Id).ToList();
                if (queryProcessingJobs.Any())
                {
                    await CheckForCompletionAsync(queryProcessingJobs, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("The reindex job was canceled, Id: {Id}", _jobInfo.Id);
                AddErrorResult(OperationOutcomeConstants.IssueSeverity.Information, OperationOutcomeConstants.IssueType.Informational, "Reindex job was cancelled by caller.");
            }
            catch (Exception ex)
            {
                HandleException(ex);
            }

            return JsonConvert.SerializeObject(_currentResult);
        }

        private async Task SyncSearchParameterStatusupdates(CancellationToken cancellationToken)
        {
            try
            {
                await _searchParameterOperations.GetAndApplySearchParameterUpdates(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogDebug("Reindex orchestator job canceled for job Id: {Id}.", _jobInfo.Id);
            }
            catch (Exception ex)
            {
                // The job failed.
                _logger.LogError(ex, "Error querying latest SearchParameterStatus updates for job Id: {Id}.", _jobInfo.Id);
            }
        }

        private async Task<IReadOnlyList<long>> CreateReindexProcessingJobsAsync(CancellationToken cancellationToken)
        {
            // Build queries based on new search params
            // Find search parameters not in a final state such as supported, pendingDelete, pendingDisable.
            List<SearchParameterStatus> validStatus = new List<SearchParameterStatus>() { SearchParameterStatus.Supported, SearchParameterStatus.PendingDelete, SearchParameterStatus.PendingDisable };
            var searchParamStatusCollection = await _searchParameterStatusManager.GetAllSearchParameterStatus(cancellationToken);
            var possibleNotYetIndexedParams = _searchParameterDefinitionManager.AllSearchParameters.Where(sp => validStatus.Contains(searchParamStatusCollection.First(p => p.Uri == sp.Url).Status));
            var notYetIndexedParams = new List<SearchParameterInfo>();

            var resourceList = new HashSet<string>();

            // filter list of SearchParameters by the target resource types
            if (_reindexJobRecord.TargetResourceTypes.Any())
            {
                foreach (var searchParam in possibleNotYetIndexedParams)
                {
                    var searchParamResourceTypes = GetDerivedResourceTypes(searchParam.BaseResourceTypes);
                    var matchingResourceTypes = searchParamResourceTypes.Intersect(_reindexJobRecord.TargetResourceTypes);
                    if (matchingResourceTypes.Any())
                    {
                        notYetIndexedParams.Add(searchParam);

                        // add matching resource types to the set of resource types which we will reindex
                        resourceList.UnionWith(matchingResourceTypes);
                    }
                    else
                    {
                        _logger.LogInformation("Search parameter {Url} is not being reindexed as it does not match the target types of reindex job Id: {Reindexid}.", searchParam.Url, _jobInfo.Id);
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
                    resourceList.UnionWith(searchParamResourceTypes);
                }
            }

            // if there are not any parameters which are supported but not yet indexed, then we have nothing to do
            if (!notYetIndexedParams.Any() && resourceList.Count == 0)
            {
                AddErrorResult(
                    OperationOutcomeConstants.IssueSeverity.Information,
                    OperationOutcomeConstants.IssueType.Informational,
                    string.Format("There are no search parameters to reindex for job Id: {0}.", _jobInfo.Id));
                return null;
            }

            // Save the list of resource types in the reindexjob document
            foreach (var resource in resourceList)
            {
                _reindexJobRecord.Resources.Add(resource);
            }

            // save the list of search parameters to the reindexjob document
            foreach (var searchParams in notYetIndexedParams.Select(p => p.Url.OriginalString))
            {
                _reindexJobRecord.SearchParams.Add(searchParams);
            }

            await CalculateAndSetTotalAndResourceCounts();

            if (!CheckJobRecordForAnyWork())
            {
                // TODO: Although we have no jobs to process, should be marking any Supported Params as Enabled if we reach this point?
                return null;
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
            var resourcesPerJob = (int)_reindexJobRecord.MaximumNumberOfResourcesPerQuery;
            var definitions = new List<string>();

            foreach (var resourceTypeEntry in _reindexJobRecord.ResourceCounts)
            {
                var resourceType = resourceTypeEntry.Key;
                var resourceCount = resourceTypeEntry.Value;

                if (resourceCount.Count <= 0)
                {
                    continue; // Skip if there are no resources to process
                }

                // Create a list to store the ranges for processing
                List<(long StartId, long EndId)> processingRanges = new List<(long StartId, long EndId)>();

                // Determine support for surrogate ID ranging once
                // This is to ensure Gen1 Reindex still works as expected but we still maintain perf on job inseration to SQL
                if (_isSurrogateIdRangingSupported == null)
                {
                    using (IScoped<ISearchService> searchService = _searchServiceFactory())
                    {
                        // Check if the implementation is SqlServerSearchService
                        Type serviceType = searchService.Value.GetType();

                        _isSurrogateIdRangingSupported = serviceType.FullName.Contains("SqlServerSearchService", StringComparison.Ordinal);

                        _logger.LogInformation(
                            _isSurrogateIdRangingSupported.Value
                                ? "Using SQL Server search service with surrogate ID ranging support"
                                : "Using search service without surrogate ID ranging support (likely Cosmos DB)");
                    }
                }

                // Check if surrogate ID ranging hasn't been determined yet or is supported
                if (_isSurrogateIdRangingSupported == true)
                {
                    // Try to use the GetSurrogateIdRanges method (SQL Server path)
                    using (IScoped<ISearchService> searchService = _searchServiceFactory())
                    {
                        var ranges = await searchService.Value.GetSurrogateIdRanges(
                            resourceType,
                            resourceCount.StartResourceSurrogateId,
                            resourceCount.EndResourceSurrogateId,
                            resourcesPerJob,
                            (int)Math.Ceiling(resourceCount.Count / (double)resourcesPerJob),
                            true,
                            cancellationToken);

                        // If we get here, it's supported
                        _isSurrogateIdRangingSupported = true;
                        processingRanges.AddRange(ranges);
                        _logger.LogInformation("Using database-provided surrogate ID ranges for resource type {ResourceType}. Generated {Count} ranges.", resourceType, ranges.Count);
                    }
                }
                else
                {
                    // Traditional chunking approach for Cosmos or fallback
                    long totalResources = resourceCount.Count;

                    int numberOfChunks = (int)Math.Ceiling(totalResources / (double)resourcesPerJob);
                    _logger.LogInformation("Using calculated ranges for resource type {ResourceType}. Creating {Count} chunks.", resourceType, numberOfChunks);

                    // Create uniform-sized chunks based on resource count
                    for (int i = 0; i < numberOfChunks; i++)
                    {
                        processingRanges.Add((0, 0)); // For Cosmos, we don't use surrogate IDs directly
                    }
                }

                // Create job definitions from the ranges
                foreach (var range in processingRanges)
                {
                    var baseResourceCount = GetSearchResultReindex(resourceType);
                    var reindexJobPayload = new ReindexProcessingJobDefinition()
                    {
                        TypeId = (int)JobType.ReindexProcessing,
                        GroupId = _jobInfo.GroupId,
                        ResourceTypeSearchParameterHashMap = GetHashMapByResourceType(resourceType),
                        ResourceCount = new SearchResultReindex
                        {
                            StartResourceSurrogateId = range.StartId,
                            EndResourceSurrogateId = range.EndId,
                            Count = baseResourceCount.Count,
                        },
                        ResourceType = resourceType,
                        MaximumNumberOfResourcesPerQuery = _reindexJobRecord.MaximumNumberOfResourcesPerQuery,
                        MaximumNumberOfResourcesPerWrite = _reindexJobRecord.MaximumNumberOfResourcesPerWrite,
                        SearchParameterUrls = _reindexJobRecord.SearchParams.ToImmutableList(),
                    };

                    definitions.Add(JsonConvert.SerializeObject(reindexJobPayload));
                }
            }

            try
            {
                var jobIds = await _timeoutRetries.ExecuteAsync(async () => (await _queueClient.EnqueueAsync((byte)QueueType.Reindex, definitions.ToArray(), _jobInfo.GroupId, false, cancellationToken))
                    .Select(job => job.Id)
                    .OrderBy(id => id)
                    .ToList());

                _logger.LogInformation("Enqueued {Count} query processing jobs. Job ID: {Id}, Group ID: {GroupId}.", jobIds.Count, _jobInfo.Id, _jobInfo.GroupId);
                return jobIds;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to enqueue jobs. Job ID: {Id}", _jobInfo.Id);
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

                SearchResult searchResult = await GetResourceCountForQueryAsync(queryForCount, countOnly: true, _cancellationToken);
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

            _jobInfo.Data = totalCount;
            _reindexJobRecord.Count = totalCount;
        }

        private async Task<SearchResult> GetResourceCountForQueryAsync(ReindexJobQueryStatus queryStatus, bool countOnly, CancellationToken cancellationToken)
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
                // Always use the queryStatus.StartResourceSurrogateId for the start of the range
                // and the ResourceCount.EndResourceSurrogateId for the end. The sql will determine
                // how many resources to actually return based on the configured maximumNumberOfResourcesPerQuery.
                // When this function returns, it knows what the next starting value to use in
                // searching for the next block of results and will use that as the queryStatus starting point
                queryParametersList.AddRange(new[]
                {
                    Tuple.Create(KnownQueryParameterNames.EndSurrogateId, searchResultReindex.EndResourceSurrogateId.ToString()),
                    Tuple.Create(KnownQueryParameterNames.StartSurrogateId, queryStatus.StartResourceSurrogateId.ToString()),
                    Tuple.Create(KnownQueryParameterNames.GlobalEndSurrogateId, "0"),
                });

                if (!countOnly)
                {
                    queryParametersList.Add(Tuple.Create(KnownQueryParameterNames.IgnoreSearchParamHash, "true"));
                }
            }

            if (queryStatus.ContinuationToken != null)
            {
                queryParametersList.Add(Tuple.Create(KnownQueryParameterNames.ContinuationToken, queryStatus.ContinuationToken));
            }

            string searchParameterHash = string.Empty;
            if (!_reindexJobRecord.ResourceTypeSearchParameterHashMap.TryGetValue(queryStatus.ResourceType, out searchParameterHash))
            {
                searchParameterHash = string.Empty;
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
                    _logger.LogError(ex, "Error running SearchForReindexAsync for resource type {ResourceType}, job Id: {JobId}.", queryStatus.ResourceType, _jobInfo.Id);
                    queryStatus.Error = reindexJobException.Message + " : " + ex.Message;
                    LogReindexJobRecordErrorMessage();

                    throw reindexJobException;
                }
            }
        }

        private void HandleException(Exception ex)
        {
            AddErrorResult(OperationOutcomeConstants.IssueSeverity.Error, OperationOutcomeConstants.IssueType.Exception, ex.Message);
            LogReindexJobRecordErrorMessage();
        }

        private async Task UpdateSearchParameterStatus(List<JobInfo> completedJobs, CancellationToken cancellationToken)
        {
            // Check if all the resource types which are base types of the search parameter
            // were reindexed by this job. If so, then we should mark the search parameters
            // as fully reindexed
            var fullyIndexedParamUris = new List<string>();
            var searchParamStatusCollection = await _searchParameterStatusManager.GetAllSearchParameterStatus(cancellationToken);
            var reindexedSearchParameters = JsonConvert.DeserializeObject<ReindexProcessingJobDefinition>(completedJobs.First().Definition).SearchParameterUrls;

            foreach (var searchParameterUrl in reindexedSearchParameters)
            {
                // Use base search param definition manager
                var searchParamInfo = _searchParameterDefinitionManager.GetSearchParameter(searchParameterUrl);
                var spStatus = searchParamStatusCollection.FirstOrDefault(sp => sp.Uri.Equals(searchParamInfo.Url)).Status;

                switch (spStatus)
                {
                    case SearchParameterStatus.PendingDisable:
                        _logger.LogInformation("Reindex job updating the status of the fully indexed search parameter, Id: {Id}, parameter: '{ParamUri}' to Disabled.", _jobInfo.Id, searchParamInfo.Url);
                        await _searchParameterStatusManager.UpdateSearchParameterStatusAsync(new List<string>() { searchParamInfo.Url.ToString() }, SearchParameterStatus.Disabled, cancellationToken);
                        break;
                    case SearchParameterStatus.PendingDelete:
                        _logger.LogInformation("Reindex job updating the status of the fully indexed search parameter, Id: {Id}, parameter: '{ParamUri}' to Deleted.", _jobInfo.Id, searchParamInfo.Url);
                        await _searchParameterStatusManager.UpdateSearchParameterStatusAsync(new List<string>() { searchParamInfo.Url.ToString() }, SearchParameterStatus.Deleted, cancellationToken);
                        break;
                    case SearchParameterStatus.Supported:
                    case SearchParameterStatus.Enabled:
                        fullyIndexedParamUris.Add(searchParamInfo.Url.OriginalString);
                        break;
                }
            }

            if (fullyIndexedParamUris.Count > 0)
            {
                _logger.LogInformation("Reindex job updating the status of the fully indexed search parameter, Id: {Id}, parameters: '{ParamUris} to Enabled.'", _jobInfo.Id, string.Join("', '", fullyIndexedParamUris));
                await _searchParameterStatusManager.UpdateSearchParameterStatusAsync(fullyIndexedParamUris, SearchParameterStatus.Enabled, _cancellationToken);
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

        private string GetHashMapByResourceType(string resourceType)
        {
            _reindexJobRecord.ResourceTypeSearchParameterHashMap.TryGetValue(resourceType, out string searchResultHashMap);
            return searchResultHashMap;
        }

        private bool CheckJobRecordForAnyWork()
        {
            return _reindexJobRecord.Count > 0 || _reindexJobRecord.ResourceCounts.Any(e => e.Value.Count <= 0 && e.Value.StartResourceSurrogateId > 0);
        }

        private void LogReindexJobRecordErrorMessage()
        {
            _reindexJobRecord.Status = OperationStatus.Failed;
            var ser = JsonConvert.SerializeObject(_reindexJobRecord);
            _logger.LogInformation($"ReindexJob Error: Current ReindexJobRecord for reference: {ser}, id: {_jobInfo.Id}");
        }

        private async Task CheckForCompletionAsync(List<JobInfo> jobInfos, CancellationToken cancellationToken)
        {
            // Track completed jobs by their IDs and by resource type
            var processedJobIds = new HashSet<long>();
            var completedJobsByResourceType = new Dictionary<string, List<JobInfo>>();
            var activeJobs = jobInfos.Where(j =>
            {
                if (j.Id == _jobInfo.GroupId)
                {
                    return false;
                }

                // Only include jobs where TypeId == 8 (ReindexProcessingJob)
                try
                {
                    var def = JsonConvert.DeserializeObject<ReindexProcessingJobDefinition>(j.Definition);
                    return def.TypeId == 8 && (j.Status == JobStatus.Running || j.Status == JobStatus.Created);
                }
                catch
                {
                    return false;
                }
            }).ToList();

            // Track job counts and timing
            int lastActiveJobCount = activeJobs.Count;
            int unchangedCount = 0;
            int changeDetectedCount = 0;
            DateTime lastPollTime = DateTime.UtcNow;

            const int MAX_UNCHANGED_CYCLES = 3;
            const int MIN_POLL_INTERVAL_MS = 100;
            const int MAX_POLL_INTERVAL_MS = 5000;
            const int DEFAULT_POLL_INTERVAL_MS = 1000;

            int currentPollInterval = DEFAULT_POLL_INTERVAL_MS;

            while (activeJobs.Any())
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

                        _logger.LogInformation(
                            "Reindex processing jobs changed for Id: {Id}. {Count} jobs active. Polling interval: {Interval}ms", _jobInfo.Id, activeJobs.Count, currentPollInterval);

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
                        // Batch fetch jobs status
                        var updatedJobs = await _queueClient.GetJobByGroupIdAsync(
                            (byte)QueueType.Reindex,
                            _jobInfo.GroupId,
                            true,
                            linkedCts.Token);

                        // Update active jobs
                        activeJobs = updatedJobs.Where(j =>
                        {
                            if (j.Id == _jobInfo.GroupId)
                            {
                                return false;
                            }

                            // Only include jobs where TypeId == 8 (ReindexProcessingJob)
                            try
                            {
                                var def = JsonConvert.DeserializeObject<ReindexProcessingJobDefinition>(j.Definition);
                                return def.TypeId == 8 && (j.Status == JobStatus.Running || j.Status == JobStatus.Created);
                            }
                            catch
                            {
                                return false;
                            }
                        }).ToList();

                        // Process newly completed jobs by resource type
                        var newlyCompletedJobs = updatedJobs
                            .Where(j => j.Status == JobStatus.Completed && !processedJobIds.Contains(j.Id))
                            .ToList();

                        if (newlyCompletedJobs.Any())
                        {
                            // Group the newly completed jobs by resource type
                            foreach (var job in newlyCompletedJobs)
                            {
                                processedJobIds.Add(job.Id);

                                var jobDefinition = JsonConvert.DeserializeObject<ReindexProcessingJobDefinition>(job.Definition);
                                var resourceType = jobDefinition.ResourceType;

                                if (!completedJobsByResourceType.TryGetValue(resourceType, out var jobList))
                                {
                                    jobList = new List<JobInfo>();
                                    completedJobsByResourceType[resourceType] = jobList;
                                }

                                jobList.Add(job);
                            }

                            // Check for resource types with all jobs completed
                            foreach (var resourceType in completedJobsByResourceType.Keys.ToList())
                            {
                                // Check if all jobs for this resourceType are complete
                                var allJobsForResourceType = updatedJobs
                                    .Where(j =>
                                    {
                                        if (j.Id == _jobInfo.GroupId)
                                        {
                                            return false;
                                        }

                                        try
                                        {
                                            var def = JsonConvert.DeserializeObject<ReindexProcessingJobDefinition>(j.Definition);
                                            return def.ResourceType == resourceType;
                                        }
                                        catch
                                        {
                                            return false;
                                        }
                                    })
                                    .ToList();

                                if (allJobsForResourceType.All(j => j.Status == JobStatus.Completed))
                                {
                                    _logger.LogInformation("All jobs for resourceType {ResourceType} are complete. Updating search parameter status.", resourceType);

                                    // Only update search parameters for resource types that we haven't processed yet
                                    var jobsToProcess = completedJobsByResourceType[resourceType];
                                    await UpdateSearchParameterStatus(jobsToProcess, cancellationToken);

                                    // Mark these jobs as having their search parameters updated
                                    foreach (var job in jobsToProcess)
                                    {
                                        processedJobIds.Add(job.Id);
                                    }

                                    // Remove this resource type from the tracking dictionary to prevent duplicate processing
                                    completedJobsByResourceType.Remove(resourceType);
                                }
                            }
                        }

                        // Send heartbeat less frequently when stable
                        if (unchangedCount <= MAX_UNCHANGED_CYCLES)
                        {
                            await _queueClient.PutJobHeartbeatAsync(_jobInfo, cancellationToken);
                        }

                        // Track last poll time and delay before next poll
                        lastPollTime = DateTime.UtcNow;
                        await Task.Delay(
                            Math.Min(MIN_POLL_INTERVAL_MS, currentPollInterval / 10),
                            cancellationToken);
                    }
                    catch (OperationCanceledException) when (linkedCts.Token.IsCancellationRequested)
                    {
                        // Normal timeout, continue polling
                        continue;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Error checking job status for Id: {Id}. Will retry with increased interval.", _jobInfo.Id);
                    currentPollInterval = Math.Min(MAX_POLL_INTERVAL_MS, currentPollInterval * 2);
                    await Task.Delay(currentPollInterval, cancellationToken);
                }
            }

            // Fetch complete job details only once at completion
            jobInfos = (List<JobInfo>)await _queueClient.GetJobByGroupIdAsync(
                (byte)QueueType.Reindex,
                _jobInfo.GroupId,
                true,
                cancellationToken);

            // Process all jobs at the end, but filter out those that already had search parameters updated
            await ProcessCompletedJobs(
                jobInfos,
                processedJobIds,
                cancellationToken);
        }

        private async Task ProcessCompletedJobs(
            List<JobInfo> jobInfos,
            HashSet<long> processedJobIds,
            CancellationToken cancellationToken)
        {
            // Get all completed and failed jobs
            var failedJobInfos = jobInfos.Where(j => j.Status == JobStatus.Failed).ToList();
            var succeededJobInfos = jobInfos.Where(j => j.Status == JobStatus.Completed).ToList();

            if (_jobInfo.CancelRequested)
            {
                throw new OperationCanceledException("Reindex operation cancelled by customer.");
            }

            if (failedJobInfos.Any())
            {
                foreach (var failedJobInfo in failedJobInfos)
                {
                    var result = JsonConvert.DeserializeObject<ReindexProcessingJobResult>(failedJobInfo.Result);
                    _currentResult.FailedResources += result.FailedResourceCount;
                    processedJobIds.Add(failedJobInfo.Id);
                }

                string userMessage = $"{_reindexJobRecord.FailureCount} resource(s) failed to be reindexed." +
                    " Resubmit the same reindex job to finish indexing the remaining resources.";
                AddErrorResult(
                    OperationOutcomeConstants.IssueSeverity.Error,
                    OperationOutcomeConstants.IssueType.Incomplete,
                    userMessage);
                LogReindexJobRecordErrorMessage();
            }
            else
            {
                foreach (var succeededJobInfo in succeededJobInfos)
                {
                    var result = JsonConvert.DeserializeObject<ReindexProcessingJobResult>(succeededJobInfo.Result);
                    _currentResult.SucceededResources += result.SucceededResourceCount;
                    processedJobIds.Add(succeededJobInfo.Id);
                }

                (int totalCount, List<string> resourcesTypes) = await CalculateTotalCount(succeededJobInfos);
                if (totalCount != 0)
                {
                    string userMessage = $"{totalCount} resource(s) of the following type(s) failed to be reindexed: '{string.Join("', '", resourcesTypes)}'." +
                        " Resubmit the same reindex job to finish indexing the remaining resources.";
                    AddErrorResult(
                        OperationOutcomeConstants.IssueSeverity.Error,
                        OperationOutcomeConstants.IssueType.Incomplete,
                        userMessage);
                    _logger.LogWarning("{TotalCount} resource(s) of the following type(s) failed to be reindexed: '{Types}' for job id: {Id}.", totalCount, string.Join("', '", resourcesTypes), _jobInfo.Id);

                    LogReindexJobRecordErrorMessage();
                }
                else
                {
                    // Only update search parameters for jobs that haven't been processed yet
                    var unprocessedJobs = succeededJobInfos
                        .Where(job => !processedJobIds.Contains(job.Id))
                        .ToList();

                    if (unprocessedJobs.Any())
                    {
                        _logger.LogInformation("Updating search parameters for {Count} unprocessed jobs", unprocessedJobs.Count);
                        await UpdateSearchParameterStatus(unprocessedJobs, cancellationToken);
                    }
                }
            }

            // Update the final completion count and status
            _currentResult.CompletedJobs = jobInfos.Count(j => j.Status == JobStatus.Completed || j.Status == JobStatus.Failed);
            _reindexJobRecord.Status = OperationStatus.Completed;
            _logger.LogInformation("All reindex processing jobs completed for Id: {Id}. Total completed: {CompletedCount}", _jobInfo.Id, _currentResult.CompletedJobs);

            return;
        }

        /// <summary>
        /// Calculates the total count of resources and identifies resource types that failed to be reindexed.
        /// </summary>
        /// <param name="succeededJobs">List of succeeded jobs.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains a tuple with the total count of failed resources and a list of resource types.</returns>
        private async Task<(int totalCount, List<string> resourcesTypes)> CalculateTotalCount(List<JobInfo> succeededJobs)
        {
            int totalCount = 0;
            var resourcesTypes = new List<string>();
            var resourceCountsFromJobs = succeededJobs
                .Select(j => JsonConvert.DeserializeObject<ReindexProcessingJobDefinition>(j.Definition))
                .Select(r => new KeyValuePair<string, SearchResultReindex>(r.ResourceType, r.ResourceCount))
                .ToList();

            foreach (KeyValuePair<string, SearchResultReindex> resourceType in resourceCountsFromJobs)
            {
                var queryForCount = new ReindexJobQueryStatus(resourceType.Key, continuationToken: null)
                {
                    LastModified = Clock.UtcNow,
                    Status = OperationStatus.Queued,
                };

                SearchResult countOnlyResults = await GetResourceCountForQueryAsync(queryForCount, countOnly: true, _cancellationToken);

                if (countOnlyResults?.TotalCount != null)
                {
                    totalCount += countOnlyResults.TotalCount.Value;
                    resourcesTypes.Add(resourceType.Key);
                }
            }

            return (totalCount, resourcesTypes);
        }
    }
}
