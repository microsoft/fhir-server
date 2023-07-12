// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Core;
using Microsoft.Health.Extensions.DependencyInjection;
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
        private IProgress<string> _progress;

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

            try
            {
                // If we are resuming a job, we can detect that by checking the Reindex queue.
                // There should be ReindexProcessingJobs in there.
                var jobs = await _queueClient.GetJobByGroupIdAsync((byte)QueueType.Reindex, _jobInfo.GroupId, true, cancellationToken);
                var queryProcessingJobs = jobs.Where(j => j.Id != _jobInfo.Id).ToList();
                if (queryProcessingJobs.Any())
                {
                    progress.Report(string.Format("Checking to see if all reindex query processing jobs for JobId: {0}, Group Id: {1}, are complete.", _jobInfo.Id, _jobInfo.GroupId));

                    await CheckForCompletionAsync(progress, queryProcessingJobs, cancellationToken);
                }
                else
                {
                    progress.Report(string.Format("Starting reindex job with Id: {0}. Status: {1}.", _jobInfo.Id, OperationStatus.Running));
                    var jobIds = await CreateReindexProcessingJobsAsync(cancellationToken);
                    if (jobIds == null || !jobIds.Any())
                    {
                        // Nothing to process so we are done.
                        progress.Report(string.Format("Nothing to process for reindex job with Id: {0}. Status: {1}. Completing Reindex Job.", _jobInfo.Id, OperationStatus.Completed));
                        AddErrorResult(OperationOutcomeConstants.IssueSeverity.Information, OperationOutcomeConstants.IssueType.Informational, "Nothing to process. Reindex complete.");
                        return JsonConvert.SerializeObject(_currentResult);
                    }

                    // Need to requeue since we are not done processing.
                    throw new RetriableJobException(string.Format("Reindex job with Id: {0} has been started. Status: {1}.", _jobInfo.Id, OperationStatus.Running));
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("The reindex job was canceled, Id: {Id}", _jobInfo.Id);
                AddErrorResult(OperationOutcomeConstants.IssueSeverity.Information, OperationOutcomeConstants.IssueType.Timeout, "Reindex job was cancelled by caller.");
            }
            catch (RetriableJobException)
            {
                progress.Report(string.Format("Not all query processing jobs are complete. Adding back to queue. JobId: {0}, Group Id: {1}.", _jobInfo.Id, _jobInfo.GroupId));
                throw;
            }
            catch (Exception ex)
            {
                HandleException(ex);
                progress.Report(string.Format("Reindex Orchestrator job failed with exception: {0}, for JobId: {1}.", ex.Message, _jobInfo.Id));
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
            _progress.Report(message);
        }

        private async Task<IReadOnlyList<long>> EnqueueQueryProcessingJobsAsync(CancellationToken cancellationToken)
        {
            var definitions = new List<string>();
            foreach (var input in _reindexJobRecord.QueryList)
            {
                var reindexJobPayload = new ReindexProcessingJobDefinition()
                {
                    TypeId = (int)JobType.ReindexProcessing,
                    ForceReindex = _reindexJobRecord.ForceReindex,
                    ResourceTypeSearchParameterHashMap = GetHashMapByResourceType(input.Key.ResourceType),
                    ResourceCount = GetSearchResultReindex(input.Key.ResourceType),
                    ResourceType = input.Key.ResourceType,
                    MaximumNumberOfResourcesPerQuery = _reindexJobRecord.MaximumNumberOfResourcesPerQuery,
                    SearchParameterUrls = _reindexJobRecord.SearchParams.ToImmutableList(),
                };
                reindexJobPayload.ResourceCount.ContinuationToken = input.Key.ContinuationToken;

                // Finish mapping to processing job
                definitions.Add(JsonConvert.SerializeObject(reindexJobPayload));
            }

            try
            {
                var jobIds = (await _queueClient.EnqueueAsync((byte)QueueType.Reindex, definitions.ToArray(), _jobInfo.GroupId, false, false, cancellationToken)).Select(_ => _.Id).OrderBy(_ => _).ToList();
                _logger.LogInformation("Enqueued {Count} query processing jobs. job id: {Id}, group id: {GroupId}.", jobIds.Count, _jobInfo.Id, _jobInfo.GroupId);
                _progress.Report($"Enqueued {jobIds.Count} query processing jobs.");
                return jobIds;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to enqueue jobs. job id: {Id}", _jobInfo.Id);
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

        private async Task<SearchResult> GetResourceCountForQueryAsync(ReindexJobQueryStatus queryStatus, bool countOnly, CancellationToken cancellationToken)
        {
            SearchResultReindex searchResultReindex = GetSearchResultReindex(queryStatus.ResourceType);
            var queryParametersList = new List<Tuple<string, string>>()
            {
                Tuple.Create(KnownQueryParameterNames.Count, _reindexJobRecord.MaximumNumberOfResourcesPerQuery.ToString()),
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
            if (!_reindexJobRecord.ForceReindex && !_reindexJobRecord.ResourceTypeSearchParameterHashMap.TryGetValue(queryStatus.ResourceType, out searchParameterHash))
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

            _progress.Report("Updating search parameter statuses complete.");
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
            _reindexJobRecord.Status = OperationStatus.Failed;
            var ser = JsonConvert.SerializeObject(_reindexJobRecord);
            _logger.LogInformation($"ReindexJob Error: Current ReindexJobRecord for reference: {ser}, id: {_jobInfo.Id}");
        }

        private async Task CheckForCompletionAsync(IProgress<string> progress, List<JobInfo> jobInfos, CancellationToken cancellationToken)
        {
                var completedJobIds = new HashSet<long>();

                var activeJobs = jobInfos.Where(j => (j.Status == JobStatus.Running || j.Status == JobStatus.Created)).ToList();
                if (activeJobs.Any())
                {
                    progress.Report(string.Format("Reindex job status update, Id: {0}. Progress: {1} jobs active out of {2} total. Note this count could be off if child jobs have been created by the ReindexProcessingJobs.", _jobInfo.Id, activeJobs.Count, jobInfos.Count));
                    throw new RetriableJobException(string.Format("Reindex processing jobs still running for Id: {0}. Adding back to queue.", _jobInfo.Id));
                }

                var failedJobInfos = jobInfos.Where(j => j.Status == JobStatus.Failed).ToList();
                var succeededJobInfos = jobInfos.Where(j => j.Status == JobStatus.Completed).ToList();

                if (_jobInfo.CancelRequested)
                {
                    progress.Report(string.Format("Reindex job status update, Id: {0}. Cancel requested.", _jobInfo.Id));
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
                    AddErrorResult(
                        OperationOutcomeConstants.IssueSeverity.Error,
                        OperationOutcomeConstants.IssueType.Incomplete,
                        userMessage);
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

                    progress.Report(string.Format("Reindex job completed, Id: {0}. Progress: {1} jobs active out of {2} total.", _jobInfo.Id, succeededJobInfos.Count + failedJobInfos.Count, jobInfos.Count));

                    if (_reindexJobRecord.ForceReindex)
                    {
                        await UpdateSearchParameterStatus(succeededJobInfos, cancellationToken);
                    }
                    else
                    {
                        (int totalCount, List<string> resourcesTypes) = await CalculateTotalCount(succeededJobInfos);
                        if (totalCount != 0)
                        {
                            string userMessage = $"{totalCount} resource(s) of the following type(s) failed to be reindexed: '{string.Join("', '", resourcesTypes)}'." + " Resubmit the same reindex job to finish indexing the remaining resources.";
                            var errorList = new List<OperationOutcomeIssue>();
                            AddErrorResult(
                               OperationOutcomeConstants.IssueSeverity.Error,
                               OperationOutcomeConstants.IssueType.Incomplete,
                               userMessage);
                            _logger.LogWarning("{TotalCount} resource(s) of the following type(s) failed to be reindexed: '{Types}' for job id: {Id}.", totalCount, string.Join("', '", resourcesTypes), _jobInfo.Id);

                            LogReindexJobRecordErrorMessage();
                        }
                        else
                        {
                            await UpdateSearchParameterStatus(succeededJobInfos, cancellationToken);
                        }
                    }
                }

                _currentResult.CompletedJobs += completedJobIds.Count;
                progress.Report(JsonConvert.SerializeObject(_currentResult));
        }

        /// <summary>
        /// Gets called from <see cref="CheckJobCompletionStatus"/> and only gets called when all ReindexProcessingJobs have completed.
        /// </summary>
        /// <param name="succeededJobs">List of succeeded jobs</param>
        /// <returns>Task<(int totalCount, List<string></returns>
        private async Task<(int totalCount, List<string> resourcesTypes)> CalculateTotalCount(List<JobInfo> succeededJobs)
        {
            int totalCount = 0;
            var resourcesTypes = new List<string>();
            var resourceCountsFromJobs = succeededJobs.Select(j => JsonConvert.DeserializeObject<ReindexProcessingJobDefinition>(j.Definition)).Select(r => new KeyValuePair<string, SearchResultReindex>(r.ResourceType, r.ResourceCount)).ToList();
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
