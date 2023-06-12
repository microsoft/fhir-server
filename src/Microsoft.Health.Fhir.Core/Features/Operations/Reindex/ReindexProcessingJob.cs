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
        private CancellationToken _cancellationToken;
        private readonly ISearchParameterOperations _searchParameterOperations;
        private JobInfo _jobInfo;
        private ReindexProcessingJobResult _reindexProcessingJobResult;
        private ReindexProcessingJobDefinition _reindexProcessingJobDefinition;
        private readonly IReindexJobThrottleController _throttleController;
        private IQueueClient _queueClient;

        public ReindexProcessingJob(
            IFhirOperationDataStore operationDatastore,
            IOptions<ReindexJobConfiguration> reindexConfiguration,
            Func<IScoped<ISearchService>> searchServiceFactory,
            ISearchParameterDefinitionManager searchParameterDefinitionManager,
            IReindexUtilities reindexUtilities,
            RequestContextAccessor<IFhirRequestContext> fhirRequestContextAccessor,
            IModelInfoProvider modelInfoProvider,
            IReindexJobThrottleController throttleController,
            SearchParameterStatusManager searchParameterStatusManager,
            ISearchParameterOperations searchParameterOperations,
            ILoggerFactory loggerFactory,
            IQueueClient queueClient)
        {
            EnsureArg.IsNotNull(operationDatastore, nameof(operationDatastore));
            EnsureArg.IsNotNull(reindexConfiguration, nameof(reindexConfiguration));
            EnsureArg.IsNotNull(loggerFactory, nameof(loggerFactory));
            EnsureArg.IsNotNull(searchServiceFactory, nameof(searchServiceFactory));
            EnsureArg.IsNotNull(searchParameterDefinitionManager, nameof(searchParameterDefinitionManager));
            EnsureArg.IsNotNull(reindexUtilities, nameof(reindexUtilities));
            EnsureArg.IsNotNull(fhirRequestContextAccessor, nameof(fhirRequestContextAccessor));
            EnsureArg.IsNotNull(modelInfoProvider, nameof(modelInfoProvider));
            EnsureArg.IsNotNull(throttleController, nameof(throttleController));
            EnsureArg.IsNotNull(searchParameterStatusManager, nameof(searchParameterStatusManager));
            EnsureArg.IsNotNull(searchParameterOperations, nameof(searchParameterOperations));
            EnsureArg.IsNotNull(queueClient, nameof(queueClient));

            _reindexJobConfiguration = reindexConfiguration.Value;
            _fhirOperationDataStore = operationDatastore;
            _logger = loggerFactory.CreateLogger<ReindexOrchestratorJob>();
            _searchServiceFactory = searchServiceFactory;
            _searchParameterDefinitionManager = searchParameterDefinitionManager;
            _reindexUtilities = reindexUtilities;
            _contextAccessor = fhirRequestContextAccessor;
            _modelInfoProvider = modelInfoProvider;
            _throttleController = throttleController;
            _searchParameterStatusManager = searchParameterStatusManager;
            _searchParameterOperations = searchParameterOperations;
            _queueClient = queueClient;
        }

        public async Task<string> ExecuteAsync(JobInfo jobInfo, IProgress<string> progress, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(jobInfo, nameof(jobInfo));
            EnsureArg.IsNotNull(progress, nameof(progress));
            _jobInfo = jobInfo;
            _reindexProcessingJobDefinition = JsonConvert.DeserializeObject<ReindexProcessingJobDefinition>(string.IsNullOrEmpty(jobInfo.Result) ? jobInfo.Definition : jobInfo.Result);
            _reindexProcessingJobResult = new ReindexProcessingJobResult();

            /*
            if (_reindexProcessingJobDefinition.TargetDataStoreUsagePercentage != null &&
                    _reindexProcessingJobDefinition.TargetDataStoreUsagePercentage > 0)
            {
                using (IScoped<IFhirDataStore> store = _fhirDataStoreFactory.Invoke())
                {
                    var provisionedCapacity = await store.Value.GetProvisionedDataStoreCapacityAsync(_cancellationToken);
                    _throttleController.Initialize(_reindexProcessingJobDefinition, provisionedCapacity);
                }
            }
            else
            {
                _throttleController.Initialize(_reindexProcessingJobDefinition, null);
            }
            */

            return JsonConvert.SerializeObject(await ProcessQueryAsync(cancellationToken));
        }

        private async Task<SearchResult> GetResourcesToReindexAsync(SearchResultReindex searchResultReindex, CancellationToken cancellationToken)
        {
            var queryParametersList = new List<Tuple<string, string>>()
            {
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
                    // This EndResourceSurrogateId is only needed because of the way the sql is written. It is not populated initially.
                    Tuple.Create(KnownQueryParameterNames.EndSurrogateId, searchResultReindex.EndResourceSurrogateId.ToString()),
                    Tuple.Create(KnownQueryParameterNames.StartSurrogateId, searchResultReindex.StartResourceSurrogateId.ToString()),
                    Tuple.Create(KnownQueryParameterNames.GlobalEndSurrogateId, "0"),
                });

                queryParametersList.Add(Tuple.Create(KnownQueryParameterNames.IgnoreSearchParamHash, "true"));
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

        private async Task<ReindexJobQueryStatus> ProcessQueryAsync(CancellationToken cancellationToken)
        {
            try
            {
                long currentResourceSurrogateId = 0;
                SearchResult result = await GetResourcesToReindexAsync(_reindexProcessingJobDefinition.ResourceCount, cancellationToken);

                if (result?.MaxResourceSurrogateId > 0)
                {
                    currentResourceSurrogateId = result.MaxResourceSurrogateId;

                    // reindex has more work to do at this point
                    // if the current is less than the max then we have more to do
                    if (result.MaxResourceSurrogateId < _reindexProcessingJobDefinition.ResourceCount.EndResourceSurrogateId)
                    {
                        // since reindex won't have a continuation token we need to check that
                        // we have more to query by checking MaxResourceSurrogateId
                        var nextQuery = new ReindexJobQueryStatus(_reindexProcessingJobDefinition.ResourceType, null)
                        {
                            LastModified = Clock.UtcNow,
                            Status = OperationStatus.Queued,
                            StartResourceSurrogateId = result.MaxResourceSurrogateId + 1,
                        };
                        _reindexProcessingJobDefinition.StartResourceSurrogateId = currentResourceSurrogateId;
                        var generatedJobId = await EnqueueChildQueryProcessingJobAsync(cancellationToken);
                        _logger.LogInformation("Reindex processing job created a child query to finish processing, job id: {JobId}, group id: {GroupId}. New job id: {NewJobId}", _jobInfo.Id, _jobInfo.GroupId, generatedJobId.FirstOrDefault());
                    }
                }

                var dictionary = new Dictionary<string, string>();
                dictionary.Add(_reindexProcessingJobDefinition.ResourceType, _reindexProcessingJobDefinition.ResourceTypeSearchParameterHashMap);

                await _reindexUtilities.ProcessSearchResultsAsync(result, dictionary, cancellationToken);

                if (!_cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        if (result != null)
                        {
                            _reindexProcessingJobResult.SucceededResourceCount += result.Results.Count();
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
