// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Core;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Operations.Reindex.Models;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Reindex
{
    public sealed class ReindexJobTask : IReindexJobTask, IDisposable
    {
        private readonly Func<IScoped<IFhirOperationDataStore>> _fhirOperationDataStoreFactory;
        private readonly Func<IScoped<IFhirDataStore>> _fhirDataStoreFactory;
        private readonly ReindexJobConfiguration _reindexJobConfiguration;
        private readonly Func<IScoped<ISearchService>> _searchServiceFactory;
        private readonly ISupportedSearchParameterDefinitionManager _supportedSearchParameterDefinitionManager;
        private readonly IReindexUtilities _reindexUtilities;
        private readonly RequestContextAccessor<IFhirRequestContext> _contextAccessor;
        private readonly IReindexJobThrottleController _throttleController;
        private readonly IModelInfoProvider _modelInfoProvider;
        private readonly ILogger _logger;

        private ReindexJobRecord _reindexJobRecord;
        private SemaphoreSlim _jobSemaphore;
        private CancellationToken _cancellationToken;
        private WeakETag _weakETag;

        public ReindexJobTask(
            Func<IScoped<IFhirOperationDataStore>> fhirOperationDataStoreFactory,
            Func<IScoped<IFhirDataStore>> fhirDataStoreFactory,
            IOptions<ReindexJobConfiguration> reindexJobConfiguration,
            Func<IScoped<ISearchService>> searchServiceFactory,
            ISupportedSearchParameterDefinitionManager supportedSearchParameterDefinitionManager,
            IReindexUtilities reindexUtilities,
            RequestContextAccessor<IFhirRequestContext> fhirRequestContextAccessor,
            IReindexJobThrottleController throttleController,
            IModelInfoProvider modelInfoProvider,
            ILogger<ReindexJobTask> logger)
        {
            EnsureArg.IsNotNull(fhirOperationDataStoreFactory, nameof(fhirOperationDataStoreFactory));
            EnsureArg.IsNotNull(fhirDataStoreFactory, nameof(fhirDataStoreFactory));
            EnsureArg.IsNotNull(reindexJobConfiguration?.Value, nameof(reindexJobConfiguration));
            EnsureArg.IsNotNull(searchServiceFactory, nameof(searchServiceFactory));
            EnsureArg.IsNotNull(supportedSearchParameterDefinitionManager, nameof(supportedSearchParameterDefinitionManager));
            EnsureArg.IsNotNull(reindexUtilities, nameof(reindexUtilities));
            EnsureArg.IsNotNull(throttleController, nameof(throttleController));
            EnsureArg.IsNotNull(modelInfoProvider, nameof(modelInfoProvider));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _fhirOperationDataStoreFactory = fhirOperationDataStoreFactory;
            _fhirDataStoreFactory = fhirDataStoreFactory;
            _reindexJobConfiguration = reindexJobConfiguration.Value;
            _searchServiceFactory = searchServiceFactory;
            _supportedSearchParameterDefinitionManager = supportedSearchParameterDefinitionManager;
            _reindexUtilities = reindexUtilities;
            _contextAccessor = fhirRequestContextAccessor;
            _throttleController = throttleController;
            _modelInfoProvider = modelInfoProvider;
            _logger = logger;
        }

        /// <inheritdoc />
        public async Task ExecuteAsync(ReindexJobRecord reindexJobRecord, WeakETag weakETag, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(reindexJobRecord, nameof(reindexJobRecord));
            EnsureArg.IsNotNull(weakETag, nameof(weakETag));
            if (_reindexJobRecord != null)
            {
                throw new NotSupportedException($"{nameof(ReindexJobTask)} can work only on one {nameof(reindexJobRecord)}. Please create new {nameof(ReindexJobTask)} to process this instance of {nameof(reindexJobRecord)}");
            }

            _reindexJobRecord = reindexJobRecord;
            _weakETag = weakETag;
            _jobSemaphore = new SemaphoreSlim(1, 1);
            _cancellationToken = cancellationToken;

            var originalRequestContext = _contextAccessor.RequestContext;

            try
            {
                // Add a request context so Datastore consumption can be added
                var fhirRequestContext = new FhirRequestContext(
                    method: OperationsConstants.Reindex,
                    uriString: "$reindex",
                    baseUriString: "$reindex",
                    correlationId: _reindexJobRecord.Id,
                    requestHeaders: new Dictionary<string, StringValues>(),
                    responseHeaders: new Dictionary<string, StringValues>())
                {
                    IsBackgroundTask = true,
                    AuditEventType = OperationsConstants.Reindex,
                };

                _contextAccessor.RequestContext = fhirRequestContext;

                if (reindexJobRecord.TargetDataStoreUsagePercentage != null &&
                    reindexJobRecord.TargetDataStoreUsagePercentage > 0)
                {
                    using (IScoped<IFhirDataStore> store = _fhirDataStoreFactory.Invoke())
                    {
                        var provisionedCapacity = await store.Value.GetProvisionedDataStoreCapacityAsync(_cancellationToken);
                        _throttleController.Initialize(_reindexJobRecord, provisionedCapacity);
                    }
                }
                else
                {
                    _throttleController.Initialize(_reindexJobRecord, null);
                }

                // If we are resuming a job, we can detect that by checking the progress info from the job record.
                // If no queries have been added to the progress then this is a new job
                if (_reindexJobRecord.QueryList?.Count == 0)
                {
                    if (!await TryPopulateNewJobFields(cancellationToken))
                    {
                        return;
                    }
                }

                if (_reindexJobRecord.Status != OperationStatus.Running || _reindexJobRecord.StartTime == null)
                {
                    // update job record to running
                    _reindexJobRecord.Status = OperationStatus.Running;
                    _reindexJobRecord.StartTime = Clock.UtcNow;
                    await UpdateJobAsync();
                }

                await ProcessJob();

                await _jobSemaphore.WaitAsync(_cancellationToken);
                try
                {
                    await CheckJobCompletionStatus();
                }
                finally
                {
                    _jobSemaphore.Release();
                }
            }
            catch (JobConflictException)
            {
                // The reindex job was updated externally.
                _logger.LogInformation("The job was updated by another process.");
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("The reindex job was canceled.");
            }
            catch (Exception ex)
            {
                await HandleException(ex);
            }
            finally
            {
                _jobSemaphore.Dispose();
                _contextAccessor.RequestContext = originalRequestContext;
            }
        }

        private async Task<bool> TryPopulateNewJobFields(CancellationToken cancellationToken)
        {
            // Build query based on new search params
            // Find supported, but not yet searchable params
            var possibleNotYetIndexedParams = _supportedSearchParameterDefinitionManager.GetSearchParametersRequiringReindexing();
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
                        _logger.LogInformation("Search parameter {Url} is not being reindexed as it does not match the target types of reindex job {Reindexid}.", searchParam.Url, _reindexJobRecord.Id);
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
                _reindexJobRecord.Error.Add(new OperationOutcomeIssue(
                    OperationOutcomeConstants.IssueSeverity.Information,
                    OperationOutcomeConstants.IssueType.Informational,
                    Core.Resources.NoSearchParametersNeededToBeIndexed));
                _reindexJobRecord.CanceledTime = Clock.UtcNow;
                await MoveToFinalStatusAsync(OperationStatus.Canceled);
                return false;
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
                _reindexJobRecord.Error.Add(new OperationOutcomeIssue(
                    OperationOutcomeConstants.IssueSeverity.Information,
                    OperationOutcomeConstants.IssueType.Informational,
                    Core.Resources.NoResourcesNeedToBeReindexed));
                await UpdateParametersAndCompleteJob();
                return false;
            }

            // Generate separate queries for each resource type and add them to query list.
            foreach (KeyValuePair<string, SearchResultReindex> resourceType in _reindexJobRecord.ResourceCounts.Where(e => e.Value.Count > 0))
            {
                var query = new ReindexJobQueryStatus(resourceType.Key, continuationToken: null)
                {
                    LastModified = Clock.UtcNow,
                    Status = OperationStatus.Queued,
                };
                _reindexJobRecord.QueryList.TryAdd(query, 1);
            }

            await UpdateJobAsync();

            _throttleController.UpdateDatastoreUsage();
            return true;
        }

        private async Task HandleException(Exception ex)
        {
            await _jobSemaphore.WaitAsync(_cancellationToken);
            try
            {
                _reindexJobRecord.Error.Add(new OperationOutcomeIssue(
                    OperationOutcomeConstants.IssueSeverity.Error,
                    OperationOutcomeConstants.IssueType.Exception,
                    ex.Message));

                _reindexJobRecord.FailureCount++;

                _logger.LogError(ex, "Encountered an unhandled exception. The job failure count increased to {FailureCount}.", _reindexJobRecord.FailureCount);

                await UpdateJobAsync();

                if (_reindexJobRecord.FailureCount >= _reindexJobConfiguration.ConsecutiveFailuresThreshold)
                {
                    await MoveToFinalStatusAsync(OperationStatus.Failed);
                }
                else
                {
                    _reindexJobRecord.Status = OperationStatus.Queued;
                    await UpdateJobAsync();
                }
            }
            finally
            {
                _jobSemaphore.Release();
            }
        }

        private async Task ProcessJob()
        {
            var queryTasks = new List<Task<ReindexJobQueryStatus>>();
            var queryCancellationTokens = new Dictionary<ReindexJobQueryStatus, CancellationTokenSource>();

            // while not all queries are finished
            while (_reindexJobRecord.QueryList.Keys.Where(q =>
                q.Status == OperationStatus.Queued ||
                q.Status == OperationStatus.Running).Any())
            {
                if (_reindexJobRecord.QueryList.Keys.Where(q => q.Status == OperationStatus.Queued).Any())
                {
                    // grab the next query from the list which is labeled as queued and run it
                    var query = _reindexJobRecord.QueryList.Keys.Where(q => q.Status == OperationStatus.Queued).OrderBy(q => q.LastModified).FirstOrDefault();
                    using CancellationTokenSource queryTokensSource = new CancellationTokenSource();
                    queryCancellationTokens.TryAdd(query, queryTokensSource);

                    // We don't await ProcessQuery, so query status can or can not be changed inside immediately
                    // In some cases we can go through whole loop and pick same query from query list.
                    // To prevent that we marking query as running here and not inside ProcessQuery code.
                    query.Status = OperationStatus.Running;
                    query.LastModified = Clock.UtcNow;
#pragma warning disable CS4014 // Suppressed as we want to continue execution and begin processing the next query while this continues to run
                    queryTasks.Add(ProcessQueryAsync(query, queryTokensSource.Token));
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed

                    _logger.LogInformation("Reindex job task created {Count} Tasks", queryTasks.Count);
                }

                // reset stale queries to pending
                var staleQueries = _reindexJobRecord.QueryList.Keys.Where(
                    q => q.Status == OperationStatus.Running && q.LastModified < Clock.UtcNow - _reindexJobConfiguration.JobHeartbeatTimeoutThreshold);
                foreach (var staleQuery in staleQueries)
                {
                    await _jobSemaphore.WaitAsync(_cancellationToken);
                    try
                    {
                        // if this query has a created task, cancel it
                        if (queryCancellationTokens.TryGetValue(staleQuery, out var tokenSource))
                        {
                            try
                            {
                                tokenSource.Cancel(false);
                            }
                            catch
                            {
                                // may throw exception if the task is disposed
                            }
                        }

                        staleQuery.Status = OperationStatus.Queued;
                        await UpdateJobAsync();
                    }
                    finally
                    {
                        _jobSemaphore.Release();
                    }
                }

                var averageDbConsumption = _throttleController.UpdateDatastoreUsage();
                _logger.LogInformation("Reindex average DB consumption: {AverageDbConsumption}", averageDbConsumption);
                var throttleDelayTime = _throttleController.GetThrottleBasedDelay();
                _logger.LogInformation("Reindex throttle delay: {ThrottleDelayTime}", throttleDelayTime);
                await Task.Delay(_reindexJobRecord.QueryDelayIntervalInMilliseconds + throttleDelayTime, _cancellationToken);

                // Remove all finished tasks from the collections of tasks
                // and cancellationTokens
                if (queryTasks.Count >= _reindexJobRecord.MaximumConcurrency)
                {
                    var taskArray = queryTasks.ToArray();
                    Task.WaitAny(taskArray, _cancellationToken);
                    var finishedTasks = queryTasks.Where(t => t.IsCompleted).ToArray();
                    foreach (var finishedTask in finishedTasks)
                    {
                        queryTasks.Remove(finishedTask);
                        queryCancellationTokens.Remove(await finishedTask);
                    }
                }

                // for most cases if another process updates the job (such as a DELETE request)
                // the _etag change will cause a JobConflict exception and this task will be aborted
                // but here we add one more check before attempting to mark the job as complete,
                // or starting another iteration of the loop
                await _jobSemaphore.WaitAsync();
                try
                {
                    using (IScoped<IFhirOperationDataStore> store = _fhirOperationDataStoreFactory.Invoke())
                    {
                        var wrapper = await store.Value.GetReindexJobByIdAsync(_reindexJobRecord.Id, _cancellationToken);
                        _weakETag = wrapper.ETag;
                        _reindexJobRecord.Status = wrapper.JobRecord.Status;
                    }
                }
                catch (Exception)
                {
                    // if something went wrong with fetching job status, we shouldn't fail process loop.
                }
                finally
                {
                    _jobSemaphore.Release();
                }

                // if our received CancellationToken is cancelled, or the job has been marked canceled we should
                // pass that cancellation request onto all the cancellationTokens
                // for the currently executing threads
                if (_cancellationToken.IsCancellationRequested || _reindexJobRecord.Status == OperationStatus.Canceled)
                {
                    foreach (var tokenSource in queryCancellationTokens.Values)
                    {
                        tokenSource.Cancel(false);
                    }

                    _logger.LogInformation("Reindex Job canceled.");
                    throw new OperationCanceledException("ReindexJob canceled.");
                }
            }

            Task.WaitAll(queryTasks.ToArray(), _cancellationToken);
        }

        private async Task<ReindexJobQueryStatus> ProcessQueryAsync(ReindexJobQueryStatus query, CancellationToken cancellationToken)
        {
            try
            {
                SearchResult results;

                await _jobSemaphore.WaitAsync(cancellationToken);
                try
                {
                    results = await ExecuteReindexQueryAsync(query, countOnly: false, cancellationToken);

                    // If continuation token then update next query but only if parent query haven't been in pipeline.
                    // For cases like retry or stale query we don't want to start another chain.
                    if (!string.IsNullOrEmpty(results?.ContinuationToken) && !query.CreatedChild)
                    {
                        var encodedContinuationToken = ContinuationTokenConverter.Encode(results.ContinuationToken);
                        var nextQuery = new ReindexJobQueryStatus(query.ResourceType, encodedContinuationToken)
                        {
                            LastModified = Clock.UtcNow,
                            Status = OperationStatus.Queued,
                        };
                        _reindexJobRecord.QueryList.TryAdd(nextQuery, 1);
                        query.CreatedChild = true;
                    }
                    else if (results?.MaxResourceSurrogateId > 0 && !query.CreatedChild)
                    {
                        SearchResultReindex searchResultReindex = GetSearchResultReindex(query.ResourceType);
                        searchResultReindex.CurrentResourceSurrogateId = results.MaxResourceSurrogateId;

                        // reindex has more work to do at this point
                        if (results.MaxResourceSurrogateId < searchResultReindex.EndResourceSurrogateId)
                        {
                            // since reindex won't have a continuation token we need to check that
                            // we have more to query by checking MaxResourceSurrogateId
                            var nextQuery = new ReindexJobQueryStatus(query.ResourceType, null)
                            {
                                LastModified = Clock.UtcNow,
                                Status = OperationStatus.Queued,
                                StartResourceSurrogateId = results.MaxResourceSurrogateId + 1,
                            };
                            _reindexJobRecord.QueryList.TryAdd(nextQuery, 1);
                            query.CreatedChild = true;
                        }
                    }

                    await UpdateJobAsync();
                    _throttleController.UpdateDatastoreUsage();
                }
                finally
                {
                    _jobSemaphore.Release();
                }

                _logger.LogInformation("Reindex job current thread: {ThreadId}", Environment.CurrentManagedThreadId);
                await _reindexUtilities.ProcessSearchResultsAsync(results, _reindexJobRecord.ResourceTypeSearchParameterHashMap, cancellationToken);
                _throttleController.UpdateDatastoreUsage();

                if (!_cancellationToken.IsCancellationRequested)
                {
                    await _jobSemaphore.WaitAsync(cancellationToken);
                    try
                    {
                        if (results != null)
                        {
                            _reindexJobRecord.Progress += results.Results.Count();
                            _logger.LogInformation("Reindex job updating progress, current result progress: {Progress}", _reindexJobRecord.Progress);
                        }

                        query.Status = OperationStatus.Completed;

                        // Remove oldest completed queryStatus object if count > 10
                        // to ensure reindex job document doesn't grow too large
                        if (_reindexJobRecord.QueryList.Keys.Where(q => q.Status == OperationStatus.Completed).Count() > 10)
                        {
                            var queryStatusToRemove = _reindexJobRecord.QueryList.Keys.Where(q => q.Status == OperationStatus.Completed).OrderBy(q => q.LastModified).FirstOrDefault();
                            _reindexJobRecord.QueryList.TryRemove(queryStatusToRemove, out var removedByte);
                        }

                        await UpdateJobAsync();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Reindex error occurred recording progress.");
                        throw;
                    }
                    finally
                    {
                        _jobSemaphore.Release();
                    }
                }

                return query;
            }
            catch (FhirException ex)
            {
                return await HandleQueryException(query, ex, true, cancellationToken);
            }
            catch (Exception ex)
            {
                return await HandleQueryException(query, ex, false, cancellationToken);
            }
        }

        private async Task<ReindexJobQueryStatus> HandleQueryException(ReindexJobQueryStatus query, Exception ex, bool isFhirException, CancellationToken cancellationToken)
        {
            await _jobSemaphore.WaitAsync(cancellationToken);
            try
            {
                query.Error = ex.Message;
                query.FailureCount++;
                _logger.LogError(ex, "Encountered an unhandled exception. The query failure count increased to {FailureCount}.", _reindexJobRecord.FailureCount);

                if (query.FailureCount >= _reindexJobConfiguration.ConsecutiveFailuresThreshold)
                {
                    if (isFhirException)
                    {
                        var issue = new OperationOutcomeIssue(
                            OperationOutcomeConstants.IssueSeverity.Error,
                            OperationOutcomeConstants.IssueType.Exception,
                            ex.Message);
                        _reindexJobRecord.Error.Add(issue);
                    }

                    query.Status = OperationStatus.Failed;
                }
                else
                {
                    query.Status = OperationStatus.Queued;
                }

                await UpdateJobAsync();
            }
            finally
            {
                _jobSemaphore.Release();
            }

            return query;
        }

        private async Task CheckJobCompletionStatus()
        {
            // If any query still in progress then we are not done
            if (_reindexJobRecord.QueryList.Keys.Where(q =>
                q.Status == OperationStatus.Queued ||
                q.Status == OperationStatus.Running).Any())
            {
                return;
            }
            else
            {
                // all queries marked as complete, reindex job is done, check success or failure
                if (_reindexJobRecord.QueryList.Keys.All(q => q.Status == OperationStatus.Completed))
                {
                    // Since this is a force reindex and we skip the SearchParameterHash check we can skip getting counts based
                    // on a SearchParameterHash because we never used that as a filter to start the reindex job with
                    if (_reindexJobRecord.ForceReindex)
                    {
                        await UpdateParametersAndCompleteJob();
                        return;
                    }

                    // Perform a final check to make sure there are no resources left to reindex
                    // this needs to check resource type + searchparamhash to be sure they don't match indicating
                    // the hash value has been updated and therefore should return a count of 0 using those 2 params
                    (int totalCount, List<string> resourcesTypes) = await CalculateTotalCount();
                    if (totalCount != 0)
                    {
                        string userMessage = $"{totalCount} resource(s) of the following type(s) failed to be reindexed: '{string.Join("', '", resourcesTypes)}'." + " Resubmit the same reindex job to finish indexing the remaining resources.";
                        _reindexJobRecord.Error.Add(new OperationOutcomeIssue(
                            OperationOutcomeConstants.IssueSeverity.Error,
                            OperationOutcomeConstants.IssueType.Incomplete,
                            userMessage));
                        _logger.LogError("{TotalCount} resource(s) of the following type(s) failed to be reindexed: '{Types}'.", totalCount, string.Join("', '", resourcesTypes));

                        await MoveToFinalStatusAsync(OperationStatus.Failed);
                    }
                    else
                    {
                        await UpdateParametersAndCompleteJob();
                    }
                }
                else
                {
                    await MoveToFinalStatusAsync(OperationStatus.Failed);
                    _logger.LogInformation("Reindex job did not complete successfully, id: {Id}.", _reindexJobRecord.Id);
                }
            }
        }

        private async Task<SearchResult> ExecuteReindexQueryAsync(ReindexJobQueryStatus queryStatus, bool countOnly, CancellationToken cancellationToken)
        {
            SearchResultReindex searchResultReindex = GetSearchResultReindex(queryStatus.ResourceType);
            var queryParametersList = new List<Tuple<string, string>>()
            {
                Tuple.Create(KnownQueryParameterNames.Count, _throttleController.GetThrottleBatchSize().ToString(CultureInfo.InvariantCulture)),
                Tuple.Create(KnownQueryParameterNames.Type, queryStatus.ResourceType),
            };

            // This should never be cosmos
            if (searchResultReindex != null && searchResultReindex.CurrentResourceSurrogateId > 0)
            {
                // if queryStatus has value then it's a continuation otherwise first time for the resource type
                long startResourceSurrogateId = Math.Max(queryStatus.StartResourceSurrogateId, searchResultReindex.CurrentResourceSurrogateId);

                queryParametersList.AddRange(new[]
                {
                    Tuple.Create(KnownQueryParameterNames.EndSurrogateId, searchResultReindex.EndResourceSurrogateId.ToString()),
                    Tuple.Create(KnownQueryParameterNames.StartSurrogateId, startResourceSurrogateId.ToString()),
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
                    return await searchService.Value.SearchForReindexAsync(queryParametersList, searchParameterHash, countOnly, cancellationToken, true);
                }
                catch (Exception ex)
                {
                    var message = $"Error running reindex query for resource type {queryStatus.ResourceType}.";
                    var reindexJobException = new ReindexJobException(message, ex);
                    _logger.LogError(ex, "Error running reindex query for resource type {ResourceType}", queryStatus.ResourceType);
                    queryStatus.Error = reindexJobException.Message + " : " + ex.Message;

                    throw reindexJobException;
                }
            }
        }

        private async Task MoveToFinalStatusAsync(OperationStatus finalStatus)
        {
            _reindexJobRecord.Status = finalStatus;
            _reindexJobRecord.EndTime = Clock.UtcNow;

            await UpdateJobAsync();
        }

        private async Task UpdateJobAsync()
        {
            _reindexJobRecord.LastModified = Clock.UtcNow;
            using (IScoped<IFhirOperationDataStore> store = _fhirOperationDataStoreFactory())
            {
                var wrapper = await store.Value.UpdateReindexJobAsync(_reindexJobRecord, _weakETag, _cancellationToken);
                _weakETag = wrapper.ETag;
            }
        }

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

                SearchResult searchResult = await ExecuteReindexQueryAsync(queryForCount, countOnly: true, _cancellationToken);
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
                    _reindexJobRecord.ResourceCounts.TryAdd(resourceType, new SearchResultReindex(0));
                }
            }

            _reindexJobRecord.Count = totalCount;
        }

        /// <summary>
        /// Gets called from <see cref="CheckJobCompletionStatus"/> and only gets called when all queryList items are status of completed
        /// </summary>
        /// <returns>Task<(int totalCount, List<string></returns>
        private async Task<(int totalCount, List<string> resourcesTypes)> CalculateTotalCount()
        {
            int totalCount = 0;
            var resourcesTypes = new List<string>();

            foreach (KeyValuePair<string, SearchResultReindex> resourceType in _reindexJobRecord.ResourceCounts.Where(e => e.Value.Count > 0))
            {
                var queryForCount = new ReindexJobQueryStatus(resourceType.Key, continuationToken: null)
                {
                    LastModified = Clock.UtcNow,
                    Status = OperationStatus.Queued,
                };

                SearchResult countOnlyResults = await ExecuteReindexQueryAsync(queryForCount, countOnly: true, _cancellationToken);

                if (countOnlyResults?.TotalCount != null)
                {
                    totalCount += countOnlyResults.TotalCount.Value;
                    resourcesTypes.Add(resourceType.Key);
                }
            }

            return (totalCount, resourcesTypes);
        }

        private async Task UpdateParametersAndCompleteJob()
        {
            // here we check if all the resource types which are base types of the search parameter
            // were reindexed by this job.  If so, then we should mark the search parameters
            // as fully reindexed
            var fullyIndexedParamUris = new List<string>();
            var reindexedResourcesSet = new HashSet<string>(_reindexJobRecord.Resources);
            foreach (var searchParam in _reindexJobRecord.SearchParams)
            {
                var searchParamInfo = _supportedSearchParameterDefinitionManager.GetSearchParameter(searchParam);
                if (reindexedResourcesSet.IsSupersetOf(searchParamInfo.BaseResourceTypes))
                {
                    fullyIndexedParamUris.Add(searchParam);
                }
            }

            _logger.LogTrace("Completing reindex job. Updating the status of the fully indexed search parameters: '{ParamUris}'", string.Join("', '", fullyIndexedParamUris));
            (bool success, string error) = await _reindexUtilities.UpdateSearchParameterStatus(fullyIndexedParamUris, _cancellationToken);

            if (success)
            {
                await MoveToFinalStatusAsync(OperationStatus.Completed);
                _logger.LogInformation("Reindex job successfully completed, id {Id}.", _reindexJobRecord.Id);
            }
            else
            {
                var issue = new OperationOutcomeIssue(
                    OperationOutcomeConstants.IssueSeverity.Error,
                    OperationOutcomeConstants.IssueType.Exception,
                    error);
                _reindexJobRecord.Error.Add(issue);
                _logger.LogError("{Error}", error);

                await MoveToFinalStatusAsync(OperationStatus.Failed);
            }
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

        private bool CheckJobRecordForAnyWork()
        {
            return _reindexJobRecord.Count > 0 || _reindexJobRecord.ResourceCounts.Any(e => e.Value.Count <= 0 && e.Value.StartResourceSurrogateId > 0);
        }

        public void Dispose()
        {
            _jobSemaphore?.Dispose();
            _jobSemaphore = null;
        }
    }
}
