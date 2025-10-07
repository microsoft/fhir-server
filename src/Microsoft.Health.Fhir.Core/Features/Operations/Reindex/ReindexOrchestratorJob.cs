﻿// -------------------------------------------------------------------------------------------------
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
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Extensions.DependencyInjection;
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

        private CancellationToken _cancellationToken;
        private IQueueClient _queueClient;
        private JobInfo _jobInfo;
        private ReindexJobRecord _reindexJobRecord;
        private ReindexOrchestratorJobResult _currentResult;
        private IReadOnlyCollection<ResourceSearchParameterStatus> _initialSearchParamStatusCollection;
        private static readonly AsyncPolicy _timeoutRetries = Policy
            .Handle<SqlException>(ex => ex.IsExecutionTimeout())
            .WaitAndRetryAsync(3, _ => TimeSpan.FromMilliseconds(RandomNumberGenerator.GetInt32(1000, 5000)));

        private HashSet<long> _processedJobIds = new HashSet<long>();
        private HashSet<string> _processedSearchParameters = new HashSet<string>();
        private List<JobInfo> _jobsToProcess;

        public ReindexOrchestratorJob(
            IQueueClient queueClient,
            Func<IScoped<ISearchService>> searchServiceFactory,
            ISearchParameterDefinitionManager searchParameterDefinitionManager,
            IModelInfoProvider modelInfoProvider,
            ISearchParameterStatusManager searchParameterStatusManager,
            ISearchParameterOperations searchParameterOperations,
            IFhirRuntimeConfiguration fhirRuntimeConfiguration,
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

            // Determine support for surrogate ID ranging once
            // This is to ensure Gen1 Reindex still works as expected but we still maintain perf on job inseration to SQL
            _isSurrogateIdRangingSupported = fhirRuntimeConfiguration.IsSurrogateIdRangingSupported;
            _logger.LogInformation(_isSurrogateIdRangingSupported ? "Using SQL Server search service with surrogate ID ranging support" : "Using search service without surrogate ID ranging support (likely Cosmos DB)");

            _jobsToProcess = new List<JobInfo>();
        }

        public async Task<string> ExecuteAsync(JobInfo jobInfo, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(jobInfo, nameof(jobInfo));
            _currentResult = string.IsNullOrEmpty(jobInfo.Result) ? new ReindexOrchestratorJobResult() : JsonConvert.DeserializeObject<ReindexOrchestratorJobResult>(jobInfo.Result);
            var reindexJobRecord = JsonConvert.DeserializeObject<ReindexJobRecord>(jobInfo.Definition);
            _jobInfo = jobInfo;
            _reindexJobRecord = reindexJobRecord;
            _cancellationToken = cancellationToken;

            _reindexJobRecord.Status = OperationStatus.Running;
            _jobInfo.Status = JobStatus.Running;
            _logger.LogInformation("Reindex job with Id: {Id} has been started. Status: {Status}.", _jobInfo.Id, _reindexJobRecord.Status);

            // Check for any changes to Search Parameters
            await SyncSearchParameterStatusupdates(cancellationToken);

            try
            {
                await CreateReindexProcessingJobsAsync(cancellationToken);

                var jobs = await _queueClient.GetJobByGroupIdAsync((byte)QueueType.Reindex, _jobInfo.GroupId, true, cancellationToken);

                // Get only ProcessingJobs.
                var queryProcessingJobs = jobs.Where(j => j.Id != _jobInfo.GroupId).ToList();

                if (!queryProcessingJobs.Any())
                {
                    // Nothing to process so we are done.
                    AddErrorResult(OperationOutcomeConstants.IssueSeverity.Information, OperationOutcomeConstants.IssueType.Informational, "Nothing to process. Reindex complete.");
                    return JsonConvert.SerializeObject(_currentResult);
                }

                _currentResult.CreatedJobs = queryProcessingJobs.Count;

                if (queryProcessingJobs.Any())
                {
                    await CheckForCompletionAsync(queryProcessingJobs, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("The reindex job was canceled, Id: {Id}", _jobInfo.Id);
                AddErrorResult(OperationOutcomeConstants.IssueSeverity.Information, OperationOutcomeConstants.IssueType.Informational, "Reindex job was cancelled by caller.");

                throw;
            }
            catch (Exception ex)
            {
                HandleException(ex);

                throw new JobExecutionException(ex.Message, ex, isCustomerCaused: false);
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
            _initialSearchParamStatusCollection = await _searchParameterStatusManager.GetAllSearchParameterStatus(cancellationToken);
            var possibleNotYetIndexedParams = _searchParameterDefinitionManager.AllSearchParameters.Where(sp => validStatus.Contains(_initialSearchParamStatusCollection.First(p => p.Uri == sp.Url).Status));
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
                return new List<long>();
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

            // Handle search parameters for resource types with count 0
            var resourceTypesWithZeroCount = _reindexJobRecord.ResourceCounts
                .Where(kvp => kvp.Value.Count == 0)
                .Select(kvp => kvp.Key)
                .ToList();

            if (resourceTypesWithZeroCount.Any())
            {
                var zeroCountParams = notYetIndexedParams
                    .Where(p => p.BaseResourceTypes.Any(baseType => resourceTypesWithZeroCount.Contains(baseType)))
                    .ToList();

                if (zeroCountParams.Any())
                {
                    // Update the SearchParameterStatus to Enabled so they can be used once data is loaded
                    await UpdateSearchParameterStatus(null, zeroCountParams.Select(p => p.Url.ToString()).ToList(), cancellationToken);
                }
            }

            if (!CheckJobRecordForAnyWork())
            {
                return new List<long>();
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

                // Get search parameters that are valid for this specific resource type
                var validSearchParameterUrls = GetValidSearchParameterUrlsForResourceType(resourceType);

                if (!validSearchParameterUrls.Any())
                {
                    _logger.LogWarning("No valid search parameters found for resource type {ResourceType} in reindex job {JobId}", resourceType, _jobInfo.Id);
                }

                // Create a list to store the ranges for processing
                List<(long StartId, long EndId)> processingRanges = new List<(long StartId, long EndId)>();

                // Check if surrogate ID ranging hasn't been determined yet or is supported
                if (_isSurrogateIdRangingSupported)
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
                            cancellationToken,
                            true);

                        processingRanges.AddRange(ranges);
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
                    var queryForCount = new ReindexJobQueryStatus(resourceType, continuationToken: null)
                    {
                        LastModified = Clock.UtcNow,
                        Status = OperationStatus.Queued,
                        StartResourceSurrogateId = range.StartId,
                        EndResourceSurrogateId = range.EndId,
                    };

                    SearchResult countOnlyResults = await GetResourceCountForQueryAsync(queryForCount, countOnly: true, _cancellationToken);

                    var reindexJobPayload = new ReindexProcessingJobDefinition()
                    {
                        TypeId = (int)JobType.ReindexProcessing,
                        GroupId = _jobInfo.GroupId,
                        ResourceTypeSearchParameterHashMap = GetHashMapByResourceType(resourceType),
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

                _logger.LogInformation(
                    "Created jobs for resource type {ResourceType} with {Count} valid search parameters: {SearchParams}", resourceType, validSearchParameterUrls.Count, string.Join(", ", validSearchParameterUrls));
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

            // Only set the count if it hasn't been initialized yet
            if (!_jobInfo.Data.HasValue)
            {
                _jobInfo.Data = totalCount;
            }

            if (_reindexJobRecord.Count == 0)
            {
                _reindexJobRecord.Count = totalCount;
            }
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

            _logger.LogError(ex, "ReindexJob Failed and didn't complete. GroupId {GroupId} / JobId {JobId}.", _jobInfo.GroupId, _jobInfo.Id);
        }

        private async Task UpdateSearchParameterStatus(List<JobInfo> completedJobs, List<string> readySearchParameters, CancellationToken cancellationToken)
        {
            // Check if all the resource types which are base types of the search parameter
            // were reindexed by this job. If so, then we should mark the search parameters
            // as fully reindexed
            var fullyIndexedParamUris = new List<string>();
            var searchParamStatusCollection = await _searchParameterStatusManager.GetAllSearchParameterStatus(cancellationToken);
            var reindexedSearchParameters = (readySearchParameters ?? ParseJobDefinition(completedJobs.First())?.SearchParameterUrls)
                ?.Where(url => !_processedSearchParameters.Contains(url))
                .ToList();

            if (reindexedSearchParameters == null || !reindexedSearchParameters.Any())
            {
                _logger.LogDebug("No new search parameters to process for reindex job {JobId}", _jobInfo.Id);
                return;
            }

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
                        _processedSearchParameters.Add(searchParameterUrl);
                        break;
                    case SearchParameterStatus.PendingDelete:
                        _logger.LogInformation("Reindex job updating the status of the fully indexed search parameter, Id: {Id}, parameter: '{ParamUri}' to Deleted.", _jobInfo.Id, searchParamInfo.Url);
                        await _searchParameterStatusManager.UpdateSearchParameterStatusAsync(new List<string>() { searchParamInfo.Url.ToString() }, SearchParameterStatus.Deleted, cancellationToken);
                        _processedSearchParameters.Add(searchParameterUrl);
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
            _jobInfo.Status = JobStatus.Failed;

            var ser = JsonConvert.SerializeObject(_reindexJobRecord);
            _logger.LogInformation($"ReindexJob Error: Current ReindexJobRecord for reference: {ser}, id: {_jobInfo.Id}");
        }

        private async Task CheckForCompletionAsync(List<JobInfo> jobInfos, CancellationToken cancellationToken)
        {
            // Track completed jobs by their IDs and by resource type
            var handledJobIds = new HashSet<long>();
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
                    return def.TypeId == (int)JobType.ReindexProcessing && (j.Status == JobStatus.Running || j.Status == JobStatus.Created);
                }
                catch
                {
                    return false;
                }
            }).ToList();

            // If we have no active jobs, yet we got here, this means the orchestrator
            // crashed before completing its work and all processing jobs have since completed.
            if (!activeJobs.Any())
            {
                var readySearchParameters = ProcessCompletedJobsAndDetermineReadiness(
                    jobInfos,
                    jobInfos.Where(j => j.Id != j.GroupId).ToList());

                // Update search parameter status for ready parameters
                if (readySearchParameters.Any() && _jobsToProcess.Any())
                {
                    await ProcessCompletedJobs(true, readySearchParameters, cancellationToken);
                }
            }

            // Track job counts and timing
            int lastActiveJobCount = activeJobs.Count;
            int unchangedCount = 0;
            int changeDetectedCount = 0;
            DateTime lastPollTime = DateTime.UtcNow;

            const int MAX_UNCHANGED_CYCLES = 3;
            const int MIN_POLL_INTERVAL_MS = 10000;
            const int MAX_POLL_INTERVAL_MS = 30000;
            const int DEFAULT_POLL_INTERVAL_MS = 10000;

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
                                return def.TypeId == (int)JobType.ReindexProcessing && (j.Status == JobStatus.Running || j.Status == JobStatus.Created);
                            }
                            catch
                            {
                                return false;
                            }
                        }).ToList();

                        // Process newly completed jobs by resource type
                        var newlyCompletedJobs = updatedJobs
                            .Where(j => j.Status == JobStatus.Completed && !handledJobIds.Contains(j.Id))
                            .ToList();

                        if (newlyCompletedJobs.Any())
                        {
                            // Process newly completed jobs and determine ready search parameters in one pass
                            var readySearchParameters = ProcessCompletedJobsAndDetermineReadiness(
                                newlyCompletedJobs,
                                updatedJobs.Where(j => j.Id != j.GroupId).ToList());

                            // Update search parameter status for ready parameters
                            if (readySearchParameters.Any() && _jobsToProcess.Any())
                            {
                                // Check if all jobs are complete (either Completed or Failed)
                                var allJobsComplete = !updatedJobs
                                    .Where(j => j.Id != _jobInfo.GroupId) // Exclude orchestrator job
                                    .Any(j => j.Status == JobStatus.Running || j.Status == JobStatus.Created);

                                await ProcessCompletedJobs(allJobsComplete, readySearchParameters, cancellationToken);
                            }

                            // Add newly completed job IDs to the processed set
                            foreach (var job in newlyCompletedJobs)
                            {
                                handledJobIds.Add(job.Id);
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
        }

        private async Task ProcessCompletedJobs(
            bool allJobsComplete,
            List<string> readySearchParameters,
            CancellationToken cancellationToken)
        {
            // Get all completed and failed jobs
            var failedJobInfos = _jobsToProcess.Where(j => j.Status == JobStatus.Failed).ToList();
            var succeededJobInfos = _jobsToProcess.Where(j => j.Status == JobStatus.Completed).ToList();

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
                    _processedJobIds.Add(failedJobInfo.Id);
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
                // Only handle for jobs that haven't been processed yet
                var unprocessedJobs = succeededJobInfos
                    .Where(job => !_processedJobIds.Contains(job.Id))
                    .ToList();

                if (unprocessedJobs.Any())
                {
                    foreach (var succeededJobInfo in unprocessedJobs)
                    {
                        var result = JsonConvert.DeserializeObject<ReindexProcessingJobResult>(succeededJobInfo.Result);
                        _currentResult.SucceededResources += result.SucceededResourceCount;
                    }

                    (int totalCount, List<string> resourcesTypes) = await CalculateTotalCount(unprocessedJobs);
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

                        // Remove url from valid search params
                        foreach (var resourceType in resourcesTypes)
                        {
                            var notReadySearchParamUrls = GetValidSearchParameterUrlsForResourceType(resourceType);

                            // Remove any search parameters that are not ready from the readySearchParameters list
                            if (notReadySearchParamUrls.Any() && readySearchParameters != null)
                            {
                                // Remove URLs that are found in notReadySearchParamUrls from readySearchParameters
                                var filteredReadySearchParameters = readySearchParameters
                                    .Where(url => !notReadySearchParamUrls.Contains(url))
                                    .ToList();

                                if (filteredReadySearchParameters.Count != readySearchParameters.Count)
                                {
                                    _logger.LogInformation(
                                        "Removed {RemovedCount} search parameters from ready list for resource type {ResourceType} due to incomplete reindexing. " +
                                        "Removed parameters: {RemovedParams}",
                                        readySearchParameters.Count - filteredReadySearchParameters.Count,
                                        resourceType,
                                        string.Join(", ", readySearchParameters.Except(filteredReadySearchParameters)));

                                    // Update the readySearchParameters reference
                                    readySearchParameters = filteredReadySearchParameters;
                                }
                            }
                        }
                    }

                    _logger.LogInformation("Updating search parameters for {Count} unprocessed jobs", unprocessedJobs.Count);
                    await UpdateSearchParameterStatus(unprocessedJobs, readySearchParameters, cancellationToken);

                    _processedJobIds.UnionWith(unprocessedJobs.Select(j => j.Id));
                }
            }

            if (allJobsComplete)
            {
                // Update the final completion count and status
                _currentResult.CompletedJobs += _jobsToProcess.Count(j => j.Status == JobStatus.Completed);
                _reindexJobRecord.Status = failedJobInfos.Any() ? OperationStatus.Failed : OperationStatus.Completed;
                _logger.LogInformation("All reindex processing jobs completed for Id: {Id}. Total completed: {CompletedCount}", _jobInfo.GroupId, _currentResult.CompletedJobs);
            }

            _jobsToProcess.Clear();
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
            var resourcesTypes = new HashSet<string>(); // Use HashSet to prevent duplicates

            // Extract unique resource types from jobs to avoid duplicate counting
            var uniqueResourceTypes = succeededJobs
                .Select(j => JsonConvert.DeserializeObject<ReindexProcessingJobDefinition>(j.Definition))
                .Select(jobDef => jobDef.ResourceType)
                .Distinct()
                .ToList();

            // Process each unique resource type only once
            foreach (string resourceType in uniqueResourceTypes)
            {
                var queryForCount = new ReindexJobQueryStatus(resourceType, continuationToken: null)
                {
                    LastModified = Clock.UtcNow,
                    Status = OperationStatus.Queued,
                };

                SearchResult countOnlyResults = await GetResourceCountForQueryAsync(queryForCount, countOnly: true, _cancellationToken);

                if (countOnlyResults?.TotalCount != null)
                {
                    totalCount += countOnlyResults.TotalCount.Value;
                    resourcesTypes.Add(resourceType);
                }
            }

            return (totalCount, resourcesTypes.ToList());
        }

        private bool HasSearchParameterStatusChanged(
            IReadOnlyCollection<ResourceSearchParameterStatus> initialCollection,
            IReadOnlyCollection<ResourceSearchParameterStatus> currentCollection)
        {
            if (initialCollection == null || currentCollection == null)
            {
                return true; // Consider this a change if either collection is null
            }

            // Create dictionaries for efficient lookup
            var initialLookup = initialCollection.ToDictionary(sp => sp.Uri, sp => sp.Status);
            var currentLookup = currentCollection.ToDictionary(sp => sp.Uri, sp => sp.Status);

            // Check 1: Count change indicates newly added or completely removed parameters
            if (initialLookup.Count != currentLookup.Count)
            {
                _logger.LogInformation("Search parameter count changed from {InitialCount} to {CurrentCount}", initialLookup.Count, currentLookup.Count);
                return true;
            }

            // Check 2: Status changes for initially enabled parameters
            var initialEnabledParams = initialLookup.Where(kvp => kvp.Value == SearchParameterStatus.Enabled);
            foreach (var kvp in initialEnabledParams)
            {
                if (!currentLookup.TryGetValue(kvp.Key, out var currentStatus) || currentStatus != kvp.Value)
                {
                    _logger.LogInformation("Initially enabled search parameter status changed for {Uri}: {InitialStatus} -> {CurrentStatus}", kvp.Key, kvp.Value, currentStatus.ToString());
                    return true;
                }
            }

            // Check 3: Parameters that transitioned to Deleted status
            var deletedParams = currentLookup.Where(kvp =>
                kvp.Value == SearchParameterStatus.Deleted &&
                initialLookup.ContainsKey(kvp.Key) &&
                initialLookup[kvp.Key] != SearchParameterStatus.Deleted);

            if (deletedParams.Any())
            {
                _logger.LogInformation("Search parameters transitioned to Deleted status: {DeletedParams}", string.Join(", ", deletedParams.Select(kvp => kvp.Key)));
                return true;
            }

            return false; // No relevant changes detected
        }

        /// <summary>
        /// Unified method to process completed jobs and determine which search parameters are ready for status updates.
        /// Combines job processing with search parameter readiness checking to reduce duplication.
        /// </summary>
        private List<string> ProcessCompletedJobsAndDetermineReadiness(
            List<JobInfo> newlyCompletedJobs,
            IReadOnlyList<JobInfo> allJobs)
        {
            // Check which search parameters are ready for status updates
            var readySearchParameters = new List<string>();

            foreach (var searchParamUrl in _reindexJobRecord.SearchParams)
            {
                if (IsSearchParameterFullyCompleted(searchParamUrl, allJobs))
                {
                    readySearchParameters.Add(searchParamUrl);
                    _logger.LogInformation(
                        "Search parameter {SearchParamUrl} is ready for status update - all related resource types completed",
                        searchParamUrl);
                }
            }

            return readySearchParameters;
        }

        /// <summary>
        /// Safely parses job definition with consistent error handling
        /// </summary>
        private ReindexProcessingJobDefinition ParseJobDefinition(JobInfo job)
        {
            try
            {
                return JsonConvert.DeserializeObject<ReindexProcessingJobDefinition>(job.Definition);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse job definition for job {JobId}", job.Id);
                return null;
            }
        }

        /// <summary>
        /// Determines if a search parameter has all its related resource types completed.
        /// Consolidates the logic for checking job completion across resource types.
        /// </summary>
        private bool IsSearchParameterFullyCompleted(string searchParamUrl, IReadOnlyList<JobInfo> allJobs)
        {
            try
            {
                // Get the search parameter definition
                var searchParamInfo = _searchParameterDefinitionManager.GetSearchParameter(searchParamUrl);

                // Get all resource types that this search parameter applies to
                var applicableResourceTypes = GetDerivedResourceTypes(searchParamInfo.BaseResourceTypes);

                // Filter to only resource types that are part of this reindex job
                var reindexJobResourceTypes = applicableResourceTypes.Intersect(_reindexJobRecord.Resources).ToList();

                if (!reindexJobResourceTypes.Any())
                {
                    return false; // No applicable resource types in this job
                }

                // Check if all jobs for all applicable resource types are completed
                foreach (var resourceType in reindexJobResourceTypes)
                {
                    if (!AreAllJobsForResourceTypeCompleted(resourceType, searchParamUrl, allJobs))
                    {
                        _logger.LogDebug(
                            "Search parameter {SearchParamUrl} not ready - resource type {ResourceType} still has incomplete jobs", searchParamUrl, resourceType);
                        return false;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error checking completion status for search parameter {SearchParamUrl}", searchParamUrl);
                return false;
            }
        }

        /// <summary>
        /// Checks if all jobs for a specific resource type and search parameter combination are completed.
        /// Consolidates the job filtering logic used throughout the class.
        /// </summary>
        private bool AreAllJobsForResourceTypeCompleted(string resourceType, string searchParamUrl, IReadOnlyList<JobInfo> allJobs)
        {
            var jobsForResourceType = GetJobsForResourceTypeAndSearchParameter(allJobs, resourceType, searchParamUrl);

            var ready = jobsForResourceType.Any() && jobsForResourceType.All(j => j.Status == JobStatus.Completed);

            if (ready)
            {
                _jobsToProcess = jobsForResourceType;
            }

            return ready;
        }

        /// <summary>
        /// Unified method to filter jobs by resource type and search parameter.
        /// Replaces multiple similar LINQ queries throughout the class.
        /// </summary>
        private List<JobInfo> GetJobsForResourceTypeAndSearchParameter(
            IReadOnlyList<JobInfo> allJobs,
            string resourceType,
            string searchParamUrl)
        {
            return allJobs
                .Where(j =>
                {
                    var jobDefinition = ParseJobDefinition(j);
                    return jobDefinition != null &&
                           jobDefinition.ResourceType == resourceType &&
                           jobDefinition.SearchParameterUrls.Contains(searchParamUrl);
                })
                .ToList();
        }

        /// <summary>
        /// Gets jobs that contain any of the specified search parameters
        /// </summary>
        private List<JobInfo> GetJobsForSearchParameters(List<JobInfo> jobs, List<string> searchParameterUrls)
        {
            return jobs
                .Where(job =>
                {
                    var jobDefinition = ParseJobDefinition(job);
                    return jobDefinition != null &&
                           jobDefinition.SearchParameterUrls.Any(url => searchParameterUrls.Contains(url));
                })
                .ToList();
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
                        _logger.LogWarning(ex, "Error validating search parameter {SearchParamUrl} for resource type {ResourceType}", searchParamUrl, resourceType);
                    }
                }

                return finalValidUrls;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error getting valid search parameters for resource type {ResourceType}. Using all search parameters as fallback.", resourceType);

                // Fallback to all search parameters if there's an error
                return _reindexJobRecord.SearchParams.ToList();
            }
        }
    }
}
