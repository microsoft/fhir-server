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
using Microsoft.Health.Core;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Operations.Reindex.Models;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Reindex
{
    public class ReindexJobTask : IReindexJobTask
    {
        private readonly Func<IScoped<IFhirOperationDataStore>> _fhirOperationDataStoreFactory;
        private readonly ReindexJobConfiguration _reindexJobConfiguration;
        private readonly Func<IScoped<ISearchService>> _searchServiceFactory;
        private readonly ISupportedSearchParameterDefinitionManager _supportedSearchParameterDefinitionManager;
        private readonly IReindexUtilities _reindexUtilities;
        private readonly ILogger _logger;

        private ReindexJobRecord _reindexJobRecord;
        private WeakETag _weakETag;

        public ReindexJobTask(
            Func<IScoped<IFhirOperationDataStore>> fhirOperationDataStoreFactory,
            IOptions<ReindexJobConfiguration> reindexJobConfiguration,
            Func<IScoped<ISearchService>> searchServiceFactory,
            ISupportedSearchParameterDefinitionManager supportedSearchParameterDefinitionManager,
            IReindexUtilities reindexUtilities,
            ILogger<ReindexJobTask> logger)
        {
            EnsureArg.IsNotNull(fhirOperationDataStoreFactory, nameof(fhirOperationDataStoreFactory));
            EnsureArg.IsNotNull(reindexJobConfiguration?.Value, nameof(reindexJobConfiguration));
            EnsureArg.IsNotNull(searchServiceFactory, nameof(searchServiceFactory));
            EnsureArg.IsNotNull(supportedSearchParameterDefinitionManager, nameof(supportedSearchParameterDefinitionManager));
            EnsureArg.IsNotNull(reindexUtilities, nameof(reindexUtilities));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _fhirOperationDataStoreFactory = fhirOperationDataStoreFactory;
            _reindexJobConfiguration = reindexJobConfiguration.Value;
            _searchServiceFactory = searchServiceFactory;
            _supportedSearchParameterDefinitionManager = supportedSearchParameterDefinitionManager;
            _reindexUtilities = reindexUtilities;
            _logger = logger;
        }

        /// <inheritdoc />
        public async Task ExecuteAsync(ReindexJobRecord reindexJobRecord, WeakETag weakETag, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(reindexJobRecord, nameof(reindexJobRecord));
            EnsureArg.IsNotNull(weakETag, nameof(weakETag));

            _reindexJobRecord = reindexJobRecord;
            _weakETag = weakETag;
            var jobSemaphore = new SemaphoreSlim(1, 1);

            try
            {
                if (_reindexJobRecord.Status != OperationStatus.Running)
                {
                    // update job record to running
                    _reindexJobRecord.Status = OperationStatus.Running;
                    _reindexJobRecord.StartTime = Clock.UtcNow;
                    await UpdateJobAsync(cancellationToken);
                }

                // If we are resuming a job, we can detect that by checking the progress info from the job record.
                // If no queries have been added to the progress then this is a new job
                if (_reindexJobRecord.QueryList?.Count == 0)
                {
                    // Build query based on new search params
                    // Find supported, but not yet searchable params
                    var notYetIndexedParams = _supportedSearchParameterDefinitionManager.GetSupportedButNotSearchableParams();

                    // if there are not any parameters which are supported but not yet indexed, then we have nothing to do
                    if (!notYetIndexedParams.Any())
                    {
                        _reindexJobRecord.Error.Add(new OperationOutcomeIssue(
                            OperationOutcomeConstants.IssueSeverity.Information,
                            OperationOutcomeConstants.IssueType.Informational,
                            Resources.NoSearchParametersNeededToBeIndexed));
                        _reindexJobRecord.CanceledTime = DateTimeOffset.UtcNow;
                        await CompleteJobAsync(OperationStatus.Canceled, cancellationToken);
                        return;
                    }

                    // From the param list, get the list of necessary resources which should be
                    // included in our query
                    var resourceList = new HashSet<string>();
                    foreach (var param in notYetIndexedParams)
                    {
                        if (param.TargetResourceTypes != null)
                        {
                            resourceList.UnionWith(param.TargetResourceTypes);
                        }

                        if (param.BaseResourceTypes != null)
                        {
                            resourceList.UnionWith(param.BaseResourceTypes);
                        }
                    }

                    _reindexJobRecord.Resources.AddRange(resourceList);
                    _reindexJobRecord.SearchParams.AddRange(notYetIndexedParams.Select(p => p.Url.ToString()));

                    await CalculateTotalCount(cancellationToken);

                    // Generate separate queries for each resource type and add them to query list.
                    foreach (string resourceType in _reindexJobRecord.Resources)
                    {
                        var query = new ReindexJobQueryStatus(resourceType, continuationToken: null)
                        {
                            LastModified = Clock.UtcNow,
                            Status = OperationStatus.Queued,
                        };

                        _reindexJobRecord.QueryList.Add(query);
                    }

                    await UpdateJobAsync(cancellationToken);
                }

                var queryTasks = new List<Task<ReindexJobQueryStatus>>();
                var queryCancellationTokens = new Dictionary<ReindexJobQueryStatus, CancellationTokenSource>();

                // while not all queries are finished
                while (_reindexJobRecord.QueryList.Where(q =>
                    q.Status == OperationStatus.Queued ||
                    q.Status == OperationStatus.Running).Any())
                {
                    if (_reindexJobRecord.QueryList.Where(q => q.Status == OperationStatus.Queued).Any())
                    {
                        // grab the next query from the list which is labeled as queued and run it
                        var query = _reindexJobRecord.QueryList.Where(q => q.Status == OperationStatus.Queued).OrderBy(q => q.LastModified).FirstOrDefault();
                        CancellationTokenSource queryTokensSource = new CancellationTokenSource();
                        queryCancellationTokens.Add(query, queryTokensSource);

#pragma warning disable CS4014 // Suppressed as we want to continue execution and begin processing the next query while this continues to run
                        queryTasks.Add(ProcessQueryAsync(query, jobSemaphore, queryTokensSource.Token));
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed

                        _logger.LogInformation($"Reindex job task created {queryTasks.Count} Tasks");
                    }

                    // reset stale queries to pending
                    var staleQueries = _reindexJobRecord.QueryList.Where(
                        q => q.Status == OperationStatus.Running && q.LastModified < Clock.UtcNow - _reindexJobConfiguration.JobHeartbeatTimeoutThreshold);
                    foreach (var staleQuery in staleQueries)
                    {
                        await jobSemaphore.WaitAsync();
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
                            await UpdateJobAsync(cancellationToken);
                        }
                        finally
                        {
                            jobSemaphore.Release();
                        }
                    }

                    await Task.Delay(_reindexJobConfiguration.QueryDelayIntervalInMilliseconds);

                    // Remove all finished tasks from the collections of tasks
                    // and cancellationTokens
                    if (queryTasks.Count >= reindexJobRecord.MaximumConcurrency)
                    {
                        var taskArray = queryTasks.ToArray();
                        Task.WaitAny(taskArray);
                        var finishedTasks = queryTasks.Where(t => t.IsCompleted).ToArray();
                        foreach (var finishedTask in finishedTasks)
                        {
                            queryTasks.Remove(finishedTask);
                            queryCancellationTokens.Remove(await finishedTask);
                        }
                    }

                    // if our received CancellationToken is cancelled we should
                    // pass that cancellation request onto all the cancellationTokens
                    // for the currently executing threads
                    if (cancellationToken.IsCancellationRequested)
                    {
                        foreach (var tokenSource in queryCancellationTokens.Values)
                        {
                            tokenSource.Cancel(false);
                        }
                    }
                }

                Task.WaitAll(queryTasks.ToArray());

                await jobSemaphore.WaitAsync();
                try
                {
                    await CheckJobCompletionStatus(cancellationToken);
                }
                finally
                {
                    jobSemaphore.Release();
                }
            }
            catch (JobConflictException)
            {
                // The reindex job was updated externally.
                _logger.LogInformation("The job was updated by another process.");
            }
            catch (Exception ex)
            {
                await jobSemaphore.WaitAsync();
                try
                {
                    _reindexJobRecord.Error.Add(new OperationOutcomeIssue(
                        OperationOutcomeConstants.IssueSeverity.Error,
                        OperationOutcomeConstants.IssueType.Exception,
                        ex.Message));

                    _reindexJobRecord.FailureCount++;

                    _logger.LogError(ex, $"Encountered an unhandled exception. The job failure count increased to {_reindexJobRecord.FailureCount}.");

                    await UpdateJobAsync(cancellationToken);

                    if (_reindexJobRecord.FailureCount >= _reindexJobConfiguration.ConsecutiveFailuresThreshold)
                    {
                        await CompleteJobAsync(OperationStatus.Failed, cancellationToken);
                    }
                }
                finally
                {
                    jobSemaphore.Release();
                }
            }
            finally
            {
                jobSemaphore.Dispose();
            }
        }

        private async Task<ReindexJobQueryStatus> ProcessQueryAsync(ReindexJobQueryStatus query, SemaphoreSlim jobSemaphore, CancellationToken cancellationToken)
        {
            try
            {
                SearchResult results;

                await jobSemaphore.WaitAsync();
                try
                {
                    query.Status = OperationStatus.Running;
                    query.LastModified = DateTimeOffset.UtcNow;

                    // Query first batch of resources
                    results = await ExecuteReindexQueryAsync(query, countOnly: false, cancellationToken);

                    // if continuation token then update next query
                    if (!string.IsNullOrEmpty(results.ContinuationToken))
                    {
                        var encodedContinuationToken = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(results.ContinuationToken));
                        var nextQuery = new ReindexJobQueryStatus(query.ResourceType, encodedContinuationToken)
                        {
                            LastModified = Clock.UtcNow,
                            Status = OperationStatus.Queued,
                        };
                        _reindexJobRecord.QueryList.Add(nextQuery);
                    }

                    await UpdateJobAsync(cancellationToken);
                }
                finally
                {
                    jobSemaphore.Release();
                }

                _logger.LogInformation($"Reindex job current thread: {Thread.CurrentThread.ManagedThreadId}");
                await _reindexUtilities.ProcessSearchResultsAsync(results, _reindexJobRecord.ResourceTypeSearchParameterHashMap, cancellationToken);

                if (!cancellationToken.IsCancellationRequested)
                {
                    await jobSemaphore.WaitAsync();
                    try
                    {
                        _reindexJobRecord.Progress += results.Results.Count();
                        query.Status = OperationStatus.Completed;
                        await UpdateJobAsync(cancellationToken);
                    }
                    finally
                    {
                        jobSemaphore.Release();
                    }
                }

                return query;
            }
            catch (Exception ex)
            {
                await jobSemaphore.WaitAsync();
                try
                {
                    query.Error = ex.Message;
                    query.FailureCount++;
                    _logger.LogError(ex, $"Encountered an unhandled exception. The query failure count increased to {_reindexJobRecord.FailureCount}.");

                    if (query.FailureCount >= _reindexJobConfiguration.ConsecutiveFailuresThreshold)
                    {
                        query.Status = OperationStatus.Failed;
                    }
                    else
                    {
                        query.Status = OperationStatus.Queued;
                    }

                    await UpdateJobAsync(cancellationToken);
                }
                finally
                {
                    jobSemaphore.Release();
                }

                return query;
            }
        }

        private async Task CheckJobCompletionStatus(CancellationToken cancellationToken)
        {
            // If any query still in progress then we are not done
            if (_reindexJobRecord.QueryList.Where(q =>
                q.Status == OperationStatus.Queued ||
                q.Status == OperationStatus.Running).Any())
            {
                return;
            }
            else
            {
                // all queries marked as complete, reindex job is done, check success or failure
                if (_reindexJobRecord.QueryList.All(q => q.Status == OperationStatus.Completed))
                {
                    (bool success, string error) = await _reindexUtilities.UpdateSearchParameters(_reindexJobRecord.SearchParams, cancellationToken);

                    if (success)
                    {
                        await CompleteJobAsync(OperationStatus.Completed, cancellationToken);
                        _logger.LogInformation($"Reindex job successfully completed, id {_reindexJobRecord.Id}.");
                    }
                    else
                    {
                        var issue = new OperationOutcomeIssue(
                            OperationOutcomeConstants.IssueSeverity.Error,
                            OperationOutcomeConstants.IssueType.Exception,
                            error);
                        _reindexJobRecord.Error.Add(issue);
                        _logger.LogError(error);

                        await CompleteJobAsync(OperationStatus.Failed, cancellationToken);
                    }
                }
                else
                {
                    await CompleteJobAsync(OperationStatus.Failed, cancellationToken);
                    _logger.LogInformation($"Reindex job did not complete successfully, id: {_reindexJobRecord.Id}.");
                }
            }
        }

        private async Task<SearchResult> ExecuteReindexQueryAsync(ReindexJobQueryStatus queryStatus, bool countOnly, CancellationToken cancellationToken)
        {
            var queryParametersList = new List<Tuple<string, string>>()
            {
                Tuple.Create(KnownQueryParameterNames.Count, _reindexJobConfiguration.MaximumNumberOfResourcesPerQuery.ToString(CultureInfo.InvariantCulture)),
                Tuple.Create(KnownQueryParameterNames.Type, queryStatus.ResourceType),
            };

            if (queryStatus.ContinuationToken != null)
            {
                queryParametersList.Add(Tuple.Create(KnownQueryParameterNames.ContinuationToken, queryStatus.ContinuationToken));
            }

            if (!_reindexJobRecord.ResourceTypeSearchParameterHashMap.TryGetValue(queryStatus.ResourceType, out string searchParameterHash))
            {
                searchParameterHash = string.Empty;
            }

            using (IScoped<ISearchService> searchService = _searchServiceFactory())
            {
                try
                {
                    return await searchService.Value.SearchForReindexAsync(queryParametersList, searchParameterHash, countOnly, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error running reindex query.");
                    queryStatus.Error = ex.Message;

                    throw;
                }
            }
        }

        private async Task CompleteJobAsync(OperationStatus completionStatus, CancellationToken cancellationToken)
        {
            _reindexJobRecord.Status = completionStatus;
            _reindexJobRecord.EndTime = Clock.UtcNow;

            await UpdateJobAsync(cancellationToken);
        }

        private async Task UpdateJobAsync(CancellationToken cancellationToken)
        {
            _reindexJobRecord.LastModified = Clock.UtcNow;
            using (IScoped<IFhirOperationDataStore> store = _fhirOperationDataStoreFactory())
            {
                var wrapper = await store.Value.UpdateReindexJobAsync(_reindexJobRecord, _weakETag, cancellationToken);
                _weakETag = wrapper.ETag;
            }
        }

        private async Task CalculateTotalCount(CancellationToken cancellationToken)
        {
            int totalCount = 0;
            foreach (string resourceType in _reindexJobRecord.Resources)
            {
                var queryForCount = new ReindexJobQueryStatus(resourceType, continuationToken: null)
                {
                    LastModified = Clock.UtcNow,
                    Status = OperationStatus.Queued,
                };

                // update the complete total
                SearchResult countOnlyResults = await ExecuteReindexQueryAsync(queryForCount, countOnly: true, cancellationToken);
                totalCount += countOnlyResults.TotalCount.Value;
            }

            _reindexJobRecord.Count = totalCount;
        }
    }
}
