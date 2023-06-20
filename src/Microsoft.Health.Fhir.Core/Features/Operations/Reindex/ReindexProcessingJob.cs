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
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Operations.Reindex.Models;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.JobManagement;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Reindex
{
    [JobTypeId((int)JobType.ReindexProcessing)]
    public class ReindexProcessingJob : IJob
    {
        private ILogger<ReindexOrchestratorJob> _logger;
        private readonly Func<IScoped<ISearchService>> _searchServiceFactory;
        private readonly IReindexUtilities _reindexUtilities;
        private JobInfo _jobInfo;
        private ReindexProcessingJobResult _reindexProcessingJobResult;
        private ReindexProcessingJobDefinition _reindexProcessingJobDefinition;
        private IQueueClient _queueClient;

        public ReindexProcessingJob(
            Func<IScoped<ISearchService>> searchServiceFactory,
            IReindexUtilities reindexUtilities,
            ILoggerFactory loggerFactory,
            IQueueClient queueClient)
        {
            EnsureArg.IsNotNull(loggerFactory, nameof(loggerFactory));
            EnsureArg.IsNotNull(searchServiceFactory, nameof(searchServiceFactory));
            EnsureArg.IsNotNull(reindexUtilities, nameof(reindexUtilities));
            EnsureArg.IsNotNull(queueClient, nameof(queueClient));

            _logger = loggerFactory.CreateLogger<ReindexOrchestratorJob>();
            _searchServiceFactory = searchServiceFactory;
            _reindexUtilities = reindexUtilities;
            _queueClient = queueClient;
        }

        public async Task<string> ExecuteAsync(JobInfo jobInfo, IProgress<string> progress, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(jobInfo, nameof(jobInfo));
            EnsureArg.IsNotNull(progress, nameof(progress));
            _jobInfo = jobInfo;
            _reindexProcessingJobDefinition = JsonConvert.DeserializeObject<ReindexProcessingJobDefinition>(jobInfo.Definition);
            _reindexProcessingJobResult = new ReindexProcessingJobResult();

            await ProcessQueryAsync(cancellationToken);
            return JsonConvert.SerializeObject(_reindexProcessingJobResult);
        }

        private async Task<SearchResult> GetResourcesToReindexAsync(SearchResultReindex searchResultReindex, CancellationToken cancellationToken)
        {
            var queryParametersList = new List<Tuple<string, string>>()
            {
                Tuple.Create(KnownQueryParameterNames.Count, _reindexProcessingJobDefinition.MaximumNumberOfResourcesPerQuery.ToString()),
                Tuple.Create(KnownQueryParameterNames.Type, _reindexProcessingJobDefinition.ResourceType),
            };

            if (searchResultReindex != null && searchResultReindex.CurrentResourceSurrogateId > 0)
            {
                // Always use the StartResourceSurrogateId for the start of the range
                // and the ResourceCount.EndResourceSurrogateId for the end. The sql will determine
                // how many resources to actually return based on the configured maximumNumberOfResourcesPerQuery.
                // When this function returns, it knows what the next starting value to use in
                // searching for the next block of results and will use that as the queryStatus starting point
                queryParametersList.AddRange(new[]
                {
                    // This EndResourceSurrogateId is only needed because of the way the sql is written. It is not accurate initially.
                    Tuple.Create(KnownQueryParameterNames.EndSurrogateId, searchResultReindex.EndResourceSurrogateId.ToString()),
                    Tuple.Create(KnownQueryParameterNames.StartSurrogateId, searchResultReindex.StartResourceSurrogateId.ToString()),
                    Tuple.Create(KnownQueryParameterNames.GlobalEndSurrogateId, "0"),
                });

                queryParametersList.Add(Tuple.Create(KnownQueryParameterNames.IgnoreSearchParamHash, "true"));
            }

            if (_reindexProcessingJobDefinition.ResourceCount.ContinuationToken != null)
            {
                queryParametersList.Add(Tuple.Create(KnownQueryParameterNames.ContinuationToken, _reindexProcessingJobDefinition.ResourceCount.ContinuationToken));
            }

            string searchParameterHash = string.Empty;
            if (!_reindexProcessingJobDefinition.ForceReindex && string.IsNullOrEmpty(_reindexProcessingJobDefinition.ResourceTypeSearchParameterHashMap))
            {
                searchParameterHash = string.Empty;
            }

            using (IScoped<ISearchService> searchService = _searchServiceFactory())
            {
                try
                {
                    return await searchService.Value.SearchForReindexAsync(queryParametersList, searchParameterHash, false, cancellationToken, true);
                }
                catch (Exception ex)
                {
                    var message = $"Error running reindex query for resource type {_reindexProcessingJobDefinition.ResourceType}.";
                    var reindexJobException = new ReindexJobException(message, ex);
                    _logger.LogError(ex, "Error running reindex query for resource type {ResourceType}", _reindexProcessingJobDefinition.ResourceType);
                    _reindexProcessingJobResult.Error = reindexJobException.Message + " : " + ex.Message;
                    LogReindexProcessingJobErrorMessage();

                    throw reindexJobException;
                }
            }
        }

        private void LogReindexProcessingJobErrorMessage()
        {
            var ser = JsonConvert.SerializeObject(_reindexProcessingJobDefinition);
            _logger.LogError($"ReindexProcessingJob Error: Current ReindexJobRecord: {ser}, job id: {_jobInfo.Id}, group id: {_jobInfo.GroupId}.");
        }

        private async Task ProcessQueryAsync(CancellationToken cancellationToken)
        {
            try
            {
                long currentResourceSurrogateId = 0;
                SearchResult result = await GetResourcesToReindexAsync(_reindexProcessingJobDefinition.ResourceCount, cancellationToken);

                if (result?.MaxResourceSurrogateId > 0)
                {
                    currentResourceSurrogateId = result.MaxResourceSurrogateId;

                    // reindex has more work to do at this point
                    if (result?.MaxResourceSurrogateId > 0)
                    {
                        _reindexProcessingJobDefinition.ResourceCount.CurrentResourceSurrogateId = result.MaxResourceSurrogateId;

                        if (result.MaxResourceSurrogateId < _reindexProcessingJobDefinition.ResourceCount.EndResourceSurrogateId)
                        {
                            // We need to create a child query to finish processing the reindex job
                            _reindexProcessingJobDefinition.ResourceCount.StartResourceSurrogateId = result.MaxResourceSurrogateId + 1;
                            _reindexProcessingJobDefinition.ResourceCount.ContinuationToken = result.ContinuationToken;

                            var generatedJobId = await EnqueueChildQueryProcessingJobAsync(cancellationToken);
                            _logger.LogInformation("Reindex processing job created a child query to finish processing, job id: {JobId}, group id: {GroupId}. New job id: {NewJobId}", _jobInfo.Id, _jobInfo.GroupId, generatedJobId[0]);
                        }
                    }
                }

                var dictionary = new Dictionary<string, string>();
                dictionary.Add(_reindexProcessingJobDefinition.ResourceType, _reindexProcessingJobDefinition.ResourceTypeSearchParameterHashMap);

                await _reindexUtilities.ProcessSearchResultsAsync(result, dictionary, cancellationToken);

                if (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        if (result != null)
                        {
                            _reindexProcessingJobResult.SucceededResourceCount += (long)result?.TotalCount;
                            _logger.LogInformation("Reindex processing job complete. Current number of resources indexed by this job: {Progress}, job id: {Id}", _reindexProcessingJobResult.SucceededResourceCount, _jobInfo.Id);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Reindex processing job error occurred recording progress. Job id: {Id}", _jobInfo.Id);
                        throw;
                    }
                }
            }
            catch (FhirException ex)
            {
                _logger.LogError(ex, "Reindex processing job error occurred. Job id: {Id}. Is FhirException: true", _jobInfo.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Reindex processing job error occurred. Job id: {Id}. Is FhirException: false", _jobInfo.Id);
            }
        }

        private async Task<IReadOnlyList<long>> EnqueueChildQueryProcessingJobAsync(CancellationToken cancellationToken)
        {
            var definitions = new List<string>();

            // Finish mapping to processing job
            definitions.Add(JsonConvert.SerializeObject(_reindexProcessingJobDefinition));

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

        private static SearchResultReindex GetSearchResultReindex(string resourceType, ReindexJobRecord reindexJobRecord)
        {
            reindexJobRecord.ResourceCounts.TryGetValue(resourceType, out SearchResultReindex searchResultReindex);
            return searchResultReindex;
        }
    }
}
