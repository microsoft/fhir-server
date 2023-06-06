// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Core;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
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
    [JobTypeId((int)JobType.ReindexProcessing)]
    public class ReindexProcessingJob : IJob
    {
        private ILogger<ReindexOrchestratorJob> _logger;
        private IFhirOperationDataStore _fhirOperationDataStore;
        private ReindexJobConfiguration _reindexJobConfiguration;

        // Determine if all of these will be needed.
        private readonly Func<IScoped<ISearchService>> _searchServiceFactory;
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager;
        private readonly SearchParameterStatusManager _searchParameterStatusManager;
        private readonly IReindexUtilities _reindexUtilities;
        private readonly RequestContextAccessor<IFhirRequestContext> _contextAccessor;
        private readonly IModelInfoProvider _modelInfoProvider;
        private SemaphoreSlim _jobSemaphore;
        private CancellationToken _cancellationToken;
        private readonly ISearchParameterOperations _searchParameterOperations;
        private JobInfo _jobInfo;

        public ReindexProcessingJob(
            IFhirOperationDataStore operationDatastore,
            IOptions<ReindexJobConfiguration> reindexConfiguration,
            Func<IScoped<ISearchService>> searchServiceFactory,
            ISearchParameterDefinitionManager searchParameterDefinitionManager,
            IReindexUtilities reindexUtilities,
            RequestContextAccessor<IFhirRequestContext> fhirRequestContextAccessor,
            IModelInfoProvider modelInfoProvider,
            SearchParameterStatusManager searchParameterStatusManager,
            ISearchParameterOperations searchParameterOperations,
            ILoggerFactory loggerFactory)
        {
            EnsureArg.IsNotNull(operationDatastore, nameof(operationDatastore));
            EnsureArg.IsNotNull(reindexConfiguration, nameof(reindexConfiguration));
            EnsureArg.IsNotNull(loggerFactory, nameof(loggerFactory));
            EnsureArg.IsNotNull(searchServiceFactory, nameof(searchServiceFactory));
            EnsureArg.IsNotNull(searchParameterDefinitionManager, nameof(searchParameterDefinitionManager));
            EnsureArg.IsNotNull(reindexUtilities, nameof(reindexUtilities));
            EnsureArg.IsNotNull(fhirRequestContextAccessor, nameof(fhirRequestContextAccessor));
            EnsureArg.IsNotNull(modelInfoProvider, nameof(modelInfoProvider));
            EnsureArg.IsNotNull(searchParameterStatusManager, nameof(searchParameterStatusManager));
            EnsureArg.IsNotNull(searchParameterOperations, nameof(searchParameterOperations));

            _reindexJobConfiguration = reindexConfiguration.Value;
            _fhirOperationDataStore = operationDatastore;
            _logger = loggerFactory.CreateLogger<ReindexOrchestratorJob>();
            _searchServiceFactory = searchServiceFactory;
            _searchParameterDefinitionManager = searchParameterDefinitionManager;
            _reindexUtilities = reindexUtilities;
            _contextAccessor = fhirRequestContextAccessor;
            _modelInfoProvider = modelInfoProvider;
            _searchParameterStatusManager = searchParameterStatusManager;
            _searchParameterOperations = searchParameterOperations;
        }

        public async Task<string> ExecuteAsync(JobInfo jobInfo, IProgress<string> progress, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(jobInfo, nameof(jobInfo));
            EnsureArg.IsNotNull(progress, nameof(progress));
            _jobInfo = jobInfo;
            var reindexJobRecord = JsonConvert.DeserializeObject<ReindexProcessingJobDefinition>(string.IsNullOrEmpty(jobInfo.Result) ? jobInfo.Definition : jobInfo.Result);

            return await ProcessQueryAsync(cancellationToken);
        }

        private async Task<string> GetResourcesToReindexAsync(ReindexJobRecord reindexJobRecord, CancellationToken cancellationToken)
        {
            ReindexJobQueryStatus queryStatus = reindexJobRecord.QueryList.FirstOrDefault().Key;
            SearchResultReindex searchResultReindex = GetSearchResultReindex(queryStatus.ResourceType, reindexJobRecord);
            var queryParametersList = new List<Tuple<string, string>>()
            {
                Tuple.Create(KnownQueryParameterNames.Type, queryStatus.ResourceType),
            };

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

                queryParametersList.Add(Tuple.Create(KnownQueryParameterNames.IgnoreSearchParamHash, "true"));
            }

            if (queryStatus.ContinuationToken != null)
            {
                queryParametersList.Add(Tuple.Create(KnownQueryParameterNames.ContinuationToken, queryStatus.ContinuationToken));
            }

            string searchParameterHash = string.Empty;
            if (!reindexJobRecord.ForceReindex && !reindexJobRecord.ResourceTypeSearchParameterHashMap.TryGetValue(queryStatus.ResourceType, out searchParameterHash))
            {
                searchParameterHash = string.Empty;
            }

            using (IScoped<ISearchService> searchService = _searchServiceFactory())
            {
                try
                {
                    var result = await searchService.Value.SearchForReindexAsync(queryParametersList, searchParameterHash, false, cancellationToken, true);
                    return JsonConvert.SerializeObject(result);
                }
                catch (Exception ex)
                {
                    var message = $"Error running reindex query for resource type {queryStatus.ResourceType}.";
                    var reindexJobException = new ReindexJobException(message, ex);
                    _logger.LogError(ex, "Error running reindex query for resource type {ResourceType}", queryStatus.ResourceType);
                    queryStatus.Error = reindexJobException.Message + " : " + ex.Message;
                    LogReindexJobRecordErrorMessage(reindexJobRecord);

                    throw reindexJobException;
                }
            }
        }

        private void LogReindexJobRecordErrorMessage(ReindexJobRecord reindexJobRecord)
        {
            var ser = JsonConvert.SerializeObject(reindexJobRecord);
            _logger.LogError($"ReindexProcessingJob Error: Current ReindexJobRecord: {ser}, job id: {_jobInfo.Id}, group id: {_jobInfo.GroupId}.");
        }

        private async Task<ReindexJobQueryStatus> ProcessQueryAsync(ReindexJobQueryStatus query, SearchResultReindex searchResultReindex, ReindexJobRecord reindexJobRecord, CancellationToken cancellationToken)
        {
            try
            {
                SearchResult results;

                await _jobSemaphore.WaitAsync(cancellationToken);
                try
                {
                    results = await GetResourcesToReindexAsync(reindexJobRecord, cancellationToken);

                    // If continuation token then update next query but only if parent query hasn't been in pipeline.
                    // For cases like retry or stale query we don't want to start another chain.
                    if (!string.IsNullOrEmpty(results?.ContinuationToken) && !query.CreatedChild)
                    {
                        var encodedContinuationToken = ContinuationTokenConverter.Encode(results.ContinuationToken);
                        var nextQuery = new ReindexJobQueryStatus(query.ResourceType, encodedContinuationToken)
                        {
                            LastModified = Clock.UtcNow,
                            Status = OperationStatus.Queued,
                        };
                        reindexJobRecord.QueryList.TryAdd(nextQuery, 1);
                        query.CreatedChild = true;
                    }
                    else if (results?.MaxResourceSurrogateId > 0 && !query.CreatedChild)
                    {
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
                            reindexJobRecord.QueryList.TryAdd(nextQuery, 1);
                            query.CreatedChild = true;
                        }
                    }
                }
                finally
                {
                    _jobSemaphore.Release();
                }

                _logger.LogInformation("Reindex job current thread: {ThreadId}, job id: {JobId}.", Environment.CurrentManagedThreadId, _jobInfo.Id);
                await _reindexUtilities.ProcessSearchResultsAsync(results, reindexJobRecord.ResourceTypeSearchParameterHashMap, cancellationToken);
                searchResultReindex.CountReindexed += (long)(results?.TotalCount != null ? results.TotalCount : 0);

                if (!_cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        if (results != null)
                        {
                            reindexJobRecord.Progress += results.Results.Count();
                            _logger.LogInformation("Reindex processing job complete. Current number of resources indexed by this job: {Progress}, job id: {Id}", reindexJobRecord.Progress, _jobInfo.Id);
                        }

                        query.Status = OperationStatus.Completed;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Reindex processing job error occurred recording progress. Job id: {Id}", _jobInfo.Id);
                        throw;
                    }
                }

                return query;
            }
            catch (FhirException ex)
            {
                return await HandleQueryException(reindexJobRecord, query, ex, true, cancellationToken);
            }
            catch (Exception ex)
            {
                return await HandleQueryException(reindexJobRecord, query, ex, false, cancellationToken);
            }
        }

        private async Task<ReindexJobQueryStatus> HandleQueryException(ReindexJobRecord reindexJobRecord, ReindexJobQueryStatus query, Exception ex, bool isFhirException, CancellationToken cancellationToken)
        {
            await _jobSemaphore.WaitAsync(cancellationToken);
            try
            {
                query.Error = ex.Message;
                query.FailureCount++;
                _logger.LogError(ex, "Encountered an unhandled exception. The query failure count increased to {FailureCount}. Job id: {JobId}.", reindexJobRecord.FailureCount, _jobInfo.Id);

                if (query.FailureCount >= _reindexJobConfiguration.ConsecutiveFailuresThreshold)
                {
                    if (isFhirException)
                    {
                        var issue = new OperationOutcomeIssue(
                            OperationOutcomeConstants.IssueSeverity.Error,
                            OperationOutcomeConstants.IssueType.Exception,
                            ex.Message);
                        reindexJobRecord.Error.Add(issue);
                    }

                    query.Status = OperationStatus.Failed;
                    reindexJobRecord.Status = OperationStatus.Failed;
                }
                else
                {
                    query.Status = OperationStatus.Queued;
                }

                LogReindexJobRecordErrorMessage(reindexJobRecord);
            }
            finally
            {
                _jobSemaphore.Release();
            }

            return query;
        }

        private static SearchResultReindex GetSearchResultReindex(string resourceType, ReindexJobRecord reindexJobRecord)
        {
            reindexJobRecord.ResourceCounts.TryGetValue(resourceType, out SearchResultReindex searchResultReindex);
            return searchResultReindex;
        }
    }
}
