// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Core;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Operations.Reindex.Models;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.JobManagement;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Reindex
{
    [JobTypeId((int)JobType.ReindexOrchestrator)]
    public sealed class ReindexOrchestratorJob : IJob, IDisposable
    {
        private ILogger<ReindexOrchestratorJob> _logger;
        private IFhirOperationDataStore _fhirOperationDataStore;
        private ReindexJobConfiguration _reindexJobConfiguration;

        // Determine if all of these will be needed.
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager;
        private readonly SearchParameterStatusManager _searchParameterStatusManager;
        private readonly IReindexUtilities _reindexUtilities;
        private readonly RequestContextAccessor<IFhirRequestContext> _contextAccessor;
        private readonly IModelInfoProvider _modelInfoProvider;
        private SemaphoreSlim _jobSemaphore;
        private CancellationToken _cancellationToken;
        private readonly ISearchParameterOperations _searchParameterOperations;
        private IQueueClient _queueClient;
        private JobInfo _jobInfo;
        private ReindexJobRecord _reindexJobRecord;
        private ReindexOrchestratorJobResult _currentResult;
        private ISearchService _searchService;
        private IProgress<string> _progress;

        public ReindexOrchestratorJob(
            IQueueClient queueClient,
            IFhirOperationDataStore operationDatastore,
            IOptions<ReindexJobConfiguration> reindexConfiguration,
            ISearchParameterDefinitionManager searchParameterDefinitionManager,
            IReindexUtilities reindexUtilities,
            RequestContextAccessor<IFhirRequestContext> fhirRequestContextAccessor,
            IModelInfoProvider modelInfoProvider,
            SearchParameterStatusManager searchParameterStatusManager,
            ISearchParameterOperations searchParameterOperations,
            ILoggerFactory loggerFactory,
            ISearchService searchService)
        {
            EnsureArg.IsNotNull(queueClient, nameof(queueClient));
            EnsureArg.IsNotNull(operationDatastore, nameof(operationDatastore));
            EnsureArg.IsNotNull(reindexConfiguration, nameof(reindexConfiguration));
            EnsureArg.IsNotNull(loggerFactory, nameof(loggerFactory));
            EnsureArg.IsNotNull(searchParameterDefinitionManager, nameof(searchParameterDefinitionManager));
            EnsureArg.IsNotNull(reindexUtilities, nameof(reindexUtilities));
            EnsureArg.IsNotNull(fhirRequestContextAccessor, nameof(fhirRequestContextAccessor));
            EnsureArg.IsNotNull(modelInfoProvider, nameof(modelInfoProvider));
            EnsureArg.IsNotNull(searchParameterStatusManager, nameof(searchParameterStatusManager));
            EnsureArg.IsNotNull(searchParameterOperations, nameof(searchParameterOperations));
            EnsureArg.IsNotNull(searchService, nameof(searchService));

            _queueClient = queueClient;
            _reindexJobConfiguration = reindexConfiguration.Value;
            _fhirOperationDataStore = operationDatastore;
            _logger = loggerFactory.CreateLogger<ReindexOrchestratorJob>();
            _searchParameterDefinitionManager = searchParameterDefinitionManager;
            _reindexUtilities = reindexUtilities;
            _contextAccessor = fhirRequestContextAccessor;
            _modelInfoProvider = modelInfoProvider;
            _searchParameterStatusManager = searchParameterStatusManager;
            _searchParameterOperations = searchParameterOperations;
            PollingPeriodSec = (int)_reindexJobConfiguration.JobPollingFrequency.TotalSeconds;
            _searchService = searchService;
        }

        public int PollingPeriodSec { get; set; }

        public async Task<string> ExecuteAsync(JobInfo jobInfo, IProgress<string> progress, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(jobInfo, nameof(jobInfo));
            EnsureArg.IsNotNull(progress, nameof(progress));
            _currentResult = string.IsNullOrEmpty(jobInfo.Result) ? new ReindexOrchestratorJobResult() : JsonConvert.DeserializeObject<ReindexOrchestratorJobResult>(jobInfo.Result);
            var reindexJobRecord = JsonConvert.DeserializeObject<ReindexJobRecord>(jobInfo.Definition);
            _jobInfo = jobInfo;
            _reindexJobRecord = reindexJobRecord;
            _progress = progress;
            _cancellationToken = cancellationToken;

            // Check for any changes to Search Parameters
            await SyncSearchParameterStatusupdates(cancellationToken);

            var originalRequestContext = _contextAccessor.RequestContext;

            try
            {
                // Add a request context so Datastore consumption can be added
                // TODO: This may not be needed anymore? This is for Cosmos DB
                var fhirRequestContext = new FhirRequestContext(
                    method: OperationsConstants.Reindex,
                    uriString: "$reindex",
                    baseUriString: "$reindex",
                    correlationId: reindexJobRecord.Id,
                    requestHeaders: new Dictionary<string, StringValues>(),
                    responseHeaders: new Dictionary<string, StringValues>())
                {
                    IsBackgroundTask = true,
                    AuditEventType = OperationsConstants.Reindex,
                };

                _contextAccessor.RequestContext = fhirRequestContext;

                // If we are resuming a job, we can detect that by checking the progress info from the job record.
                // If no query processing jobs have been created, then we need to start the job.
                var jobs = await _queueClient.GetJobByGroupIdAsync((byte)QueueType.Reindex, _jobInfo.GroupId, true, cancellationToken);
                if (jobs.Any())
                {
                    progress.Report(string.Format("Checking to see if all reindex query processing jobs for JobId: {0}, Group Id: {1}, are still running.", _jobInfo.Id, _jobInfo.GroupId));

                    // There may be an issue here where we are counting the orchestrator job we are currently running unless that is already popped out of the queue.
                    await CheckForCompletionAsync(progress, jobs.Select(j => j.Id).ToList(), cancellationToken);
                }
                else
                {
                    progress.Report(string.Format("Starting reindex job with Id: {0}. Status: {1}.", _jobInfo.Id, OperationStatus.Running));
                    var jobIds = await ExecuteReindexProcessingJobsAsync(cancellationToken);
                    if (jobIds == null)
                    {
                        progress.Report(JsonConvert.SerializeObject(_currentResult.Error));
                    }
                    else if (!jobIds.Any())
                    {
                        // Nothing to process so we are done.
                        progress.Report(string.Format("Nothing to process for reindex job with Id: {0}. Status: {1}.", _jobInfo.Id, OperationStatus.Completed));
                        return JsonConvert.SerializeObject(_reindexJobConfiguration);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("The reindex job was canceled, Id: {Id}", _jobInfo.Id);
            }
            catch (RetriableJobException)
            {
                progress.Report(string.Format("Not all query processing jobs are complete. Adding back to queue. JobId: {0}, Group Id: {1}.", _jobInfo.Id, _jobInfo.GroupId));
                throw;
            }
            catch (Exception ex)
            {
                await HandleException(ex);
                progress.Report(string.Format("Reindex Orchestrator job failed with exception: {0}, for JobId: {1}.", ex.Message, _jobInfo.Id));
            }
            finally
            {
                _contextAccessor.RequestContext = originalRequestContext;
            }

            return JsonConvert.SerializeObject(_reindexJobRecord);
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

        private async Task<IReadOnlyList<long>> ExecuteReindexProcessingJobsAsync(CancellationToken cancellationToken)
        {
            // Build queries based on new search params
            // Find search parameters not in a final state such as supported, pendingDelete, pendingDisable.
            List<SearchParameterStatus> validStatus = new List<SearchParameterStatus>() { SearchParameterStatus.Supported, SearchParameterStatus.PendingDelete, SearchParameterStatus.PendingDisable };
            var searchParamStatusCollection = await _searchParameterStatusManager.GetAllSearchParameterStatus(cancellationToken);
            var possibleNotYetIndexedParams = _searchParameterDefinitionManager.AllSearchParameters.Where(sp => validStatus.Contains(searchParamStatusCollection.First(p => p.Uri == sp.Url).Status) || sp.IsSearchable == false || sp.SortStatus == SortParameterStatus.Supported);
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
            else if (_reindexJobRecord.ForceReindex)
            {
                resourceList.UnionWith(_reindexJobRecord.SearchParameterResourceTypes);
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
                await UpdateSearchParameterStatus(cancellationToken);
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

        private async Task<IReadOnlyList<long>> EnqueueQueryProcessingJobsAsync(CancellationToken cancellationToken)
        {
            var definitions = new List<string>();
            foreach (var input in _reindexJobRecord.QueryList)
            {
                var reindexJobPayload = new ReindexProcessingJobDefinition()
                {
                    TypeId = (int)JobType.ReindexProcessing,
                    StartResourceSurrogateId = input.Key.StartResourceSurrogateId,
                    CreatedChild = false,
                    ForceReindex = _reindexJobRecord.ForceReindex,
                    ResourceTypeSearchParameterHashMap = GetHashMapByResourceType(input.Key.ResourceType),
                    ResourceCount = GetSearchResultReindex(input.Key.ResourceType),
                    ResourceType = input.Key.ResourceType,
                    MaximumNumberOfResourcesPerQuery = _reindexJobRecord.MaximumNumberOfResourcesPerQuery,
                    TargetDataStoreUsagePercentage = _reindexJobRecord.TargetDataStoreUsagePercentage,
                    QueryDelayIntervalInMilliseconds = _reindexJobRecord.QueryDelayIntervalInMilliseconds,
                };

                // Finish mapping to processing job
                definitions.Add(JsonConvert.SerializeObject(reindexJobPayload));
            }

            try
            {
                var jobIds = (await _queueClient.EnqueueAsync((byte)QueueType.Reindex, definitions.ToArray(), _jobInfo.GroupId, false, false, cancellationToken)).Select(_ => _.Id).OrderBy(_ => _).ToList();
                return jobIds;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to enqueue jobs.");
                throw new RetriableJobException(ex.Message, ex);
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

                SearchResult searchResult = await GetResourceCountForQueryAsync(queryForCount, _cancellationToken);
                totalCount += searchResult != null ? searchResult.TotalCount.Value : 0;

                if (searchResult?.ReindexResult?.StartResourceSurrogateId > 0)
                {
                    SearchResultReindex reindexResults = searchResult.ReindexResult;
                    _reindexJobRecord.ResourceCounts.TryAdd(resourceType, new SearchResultReindex()
                    {
                        Count = reindexResults.Count,
                        CurrentResourceSurrogateId = reindexResults.CurrentResourceSurrogateId,
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

            _reindexJobRecord.Count = totalCount;
        }

        private async Task<SearchResult> GetResourceCountForQueryAsync(ReindexJobQueryStatus queryStatus, CancellationToken cancellationToken)
        {
            SearchResultReindex searchResultReindex = GetSearchResultReindex(queryStatus.ResourceType);
            var queryParametersList = new List<Tuple<string, string>>()
            {
                Tuple.Create(KnownQueryParameterNames.Type, queryStatus.ResourceType),
            };

            // This should never be cosmos
            if (searchResultReindex != null && searchResultReindex.CurrentResourceSurrogateId > 0)
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
            }

            if (queryStatus.ContinuationToken != null)
            {
                queryParametersList.Add(Tuple.Create(KnownQueryParameterNames.ContinuationToken, queryStatus.ContinuationToken));
            }

            string searchParameterHash = string.Empty;
            if (!_reindexJobRecord.ForceReindex && !_reindexJobRecord.ResourceTypeSearchParameterHashMap.TryGetValue(queryStatus.ResourceType, out searchParameterHash))
            {
                searchParameterHash = string.Empty;
            }

            try
            {
                return await _searchService.SearchForReindexAsync(queryParametersList, searchParameterHash, countOnly: true, cancellationToken, true);
            }
            catch (Exception ex)
            {
                var message = $"Error running reindex query for resource type {queryStatus.ResourceType}.";
                var reindexJobException = new ReindexJobException(message, ex);
                _logger.LogError(ex, "Error running reindex query for resource type {ResourceType}, job Id: {JobId}.", queryStatus.ResourceType, _jobInfo.Id);
                queryStatus.Error = reindexJobException.Message + " : " + ex.Message;
                LogReindexJobRecordErrorMessage();

                throw reindexJobException;
            }
        }

        private async Task HandleException(Exception ex)
        {
            await _jobSemaphore.WaitAsync(_cancellationToken);
            try
            {
                _currentResult.Error.Add(new OperationOutcomeIssue(
                    OperationOutcomeConstants.IssueSeverity.Error,
                    OperationOutcomeConstants.IssueType.Exception,
                    ex.Message));

                _reindexJobRecord.FailureCount++;

                _logger.LogError(ex, "Encountered an unhandled exception. The job failure count increased to {FailureCount}, Id: {Id}.", _reindexJobRecord.FailureCount, _jobInfo.Id);

                LogReindexJobRecordErrorMessage();
            }
            finally
            {
                _jobSemaphore.Release();
            }
        }

        private async Task UpdateSearchParameterStatus(CancellationToken cancellationToken)
        {
            // Check if all the resource types which are base types of the search parameter
            // were reindexed by this job. If so, then we should mark the search parameters
            // as fully reindexed
            var fullyIndexedParamUris = new List<string>();
            var reindexedResourcesSet = new HashSet<string>(_reindexJobRecord.Resources);
            var searchParamStatusCollection = await _searchParameterStatusManager.GetAllSearchParameterStatus(cancellationToken);
            foreach (var searchParam in _reindexJobRecord.SearchParams)
            {
                // Use base search param definition manager
                var searchParamInfo = _searchParameterDefinitionManager.GetSearchParameter(searchParam);
                var spStatus = searchParamStatusCollection.FirstOrDefault(sp => sp.Uri.Equals(searchParamInfo.Url)).Status;
                bool allResourcesForSearchParamIndexed = reindexedResourcesSet.IsSupersetOf(searchParamInfo.BaseResourceTypes);

                // Check to see if all resources associated with the search parameter are indexed before proceeding with status updates.
                if (allResourcesForSearchParamIndexed)
                {
                    // Adding the below to explicitly update from their pending states to their final states.
                    if (spStatus == SearchParameterStatus.PendingDisable)
                    {
                        _logger.LogInformation("Reindex job updating the status of the fully indexed search, Id: {Id}, parameter: '{ParamUri}' to Disabled.", _jobInfo.Id, searchParam);
                        await _searchParameterStatusManager.UpdateSearchParameterStatusAsync(new List<string>() { searchParamInfo.Url.ToString() }, SearchParameterStatus.Disabled, cancellationToken);
                    }
                    else if (spStatus == SearchParameterStatus.PendingDelete)
                    {
                        _logger.LogInformation("Reindex job updating the status of the fully indexed search, Id: {Id}, parameter: '{ParamUri}' to Deleted.", _jobInfo.Id, searchParam);
                        await _searchParameterStatusManager.UpdateSearchParameterStatusAsync(new List<string>() { searchParamInfo.Url.ToString() }, SearchParameterStatus.Deleted, cancellationToken);
                    }
                    else if (spStatus == SearchParameterStatus.Supported || spStatus == SearchParameterStatus.Enabled)
                    {
                        fullyIndexedParamUris.Add(searchParam);
                    }
                }
            }

            _logger.LogInformation("Reindex job updating the status of the fully indexed search, Id: {Id}, parameters: '{ParamUris}'", _jobInfo.Id, string.Join("', '", fullyIndexedParamUris));
            (bool success, string error) = await _reindexUtilities.UpdateSearchParameterStatusToEnabled(fullyIndexedParamUris, _cancellationToken);
        }

        private ICollection<string> GetDerivedResourceTypes(IReadOnlyCollection<string> resourceTypes)
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
            var ser = JsonConvert.SerializeObject(_reindexJobRecord);
            _logger.LogError($"ReindexJob Error: Current ReindexJobRecord: {ser}");
        }

        private async Task CheckForCompletionAsync(IProgress<string> progress, IList<long> jobIds, CancellationToken cancellationToken)
        {
            do
            {
                var completedJobIds = new HashSet<long>();
                var jobInfos = new List<JobInfo>();
                double duration;
                try
                {
                    var start = Stopwatch.StartNew();
                    jobInfos.AddRange(await _queueClient.GetJobsByIdsAsync((byte)QueueType.Reindex, jobIds.ToArray(), false, cancellationToken));
                    duration = start.Elapsed.TotalSeconds;
                }
                catch (Exception ex)
                {
                    progress.Report(string.Format("Reindex job status update, Id: {0}. Failed to get running jobs from queue. Exception: {1}", _jobInfo.Id, ex.Message));
                    throw new RetriableJobException(ex.Message, ex);
                }

                var activeJobs = jobInfos.Where(j => (j.Status == JobStatus.Running || j.Status == JobStatus.Created) && j.Id != _jobInfo.Id).ToList();
                if (activeJobs.Any())
                {
                    progress.Report(string.Format("Reindex job status update, Id: {0}. Progress: {1} jobs active out of {2} total.", _jobInfo.Id, activeJobs.Count, jobInfos.Count));
                    throw new RetriableJobException(string.Format("Reindex processing jobs still running for Id: {0}. Adding back to queue.", _jobInfo.Id));
                }

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
                        completedJobIds.Add(failedJobInfo.Id);
                    }

                    string userMessage = $"{_reindexJobRecord.FailureCount} resource(s) failed to be reindexed." + " Resubmit the same reindex job to finish indexing the remaining resources.";
                    _currentResult.Error.Add(new OperationOutcomeIssue(
                            OperationOutcomeConstants.IssueSeverity.Error,
                            OperationOutcomeConstants.IssueType.Incomplete,
                            userMessage));

                    _logger.LogError(userMessage);

                    LogReindexJobRecordErrorMessage();
                }
                else
                {
                    foreach (var suceededJobInfo in succeededJobInfos)
                    {
                        var result = JsonConvert.DeserializeObject<ReindexProcessingJobResult>(suceededJobInfo.Result);
                        _currentResult.SucceededResources += result.SucceededResourceCount;
                        completedJobIds.Add(suceededJobInfo.Id);
                    }

                    // Since this is a force reindex and we skip the SearchParameterHash check we can skip getting counts based
                    // on a SearchParameterHash because we never used that as a filter to start the reindex job with
                    progress.Report(string.Format("Reindex job completed, Id: {0}. Progress: {1} jobs active out of {2} total.", _jobInfo.Id, succeededJobInfos.Count + failedJobInfos.Count, jobInfos.Count));

                    await UpdateSearchParameterStatus(cancellationToken);
                }

                if (completedJobIds.Count > 0)
                {
                    foreach (var jobId in completedJobIds)
                    {
                        jobIds.Remove(jobId);
                    }

                    _currentResult.CompletedJobs += completedJobIds.Count;
                    progress.Report(JsonConvert.SerializeObject(_currentResult));

                    await Task.Delay(TimeSpan.FromSeconds(duration * 10), cancellationToken); // throttle to avoid high database utilization.
                }
                else
                {
                    await Task.Delay(TimeSpan.FromSeconds(PollingPeriodSec), cancellationToken);
                }
            }
            while (jobIds.Count > 0);
        }

        public void Dispose()
        {
            _jobSemaphore?.Dispose();
            _jobSemaphore = null;
        }
    }
}
