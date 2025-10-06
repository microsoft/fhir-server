// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp.Dom;
using EnsureThat;
using Hl7.Fhir.Utility;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.JobManagement;
using Newtonsoft.Json;
using Polly;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Reindex
{
    [JobTypeId((int)JobType.ReindexProcessing)]
    public class ReindexProcessingJob : IJob
    {
        private ILogger<ReindexOrchestratorJob> _logger;
        private readonly Func<IScoped<ISearchService>> _searchServiceFactory;
        private JobInfo _jobInfo;
        private ReindexProcessingJobResult _reindexProcessingJobResult;
        private ReindexProcessingJobDefinition _reindexProcessingJobDefinition;
        private IQueueClient _queueClient;
        private const int MaxTimeoutRetries = 3;
        private Func<IScoped<IFhirDataStore>> _fhirDataStoreFactory;
        private readonly IResourceWrapperFactory _resourceWrapperFactory;
        private static readonly AsyncPolicy _timeoutRetries = Policy
            .Handle<SqlException>(ex => ex.IsExecutionTimeout())
            .WaitAndRetryAsync(MaxTimeoutRetries, _ => TimeSpan.FromMilliseconds(RandomNumberGenerator.GetInt32(1000, 5000)));

        public ReindexProcessingJob(
            Func<IScoped<ISearchService>> searchServiceFactory,
            ILoggerFactory loggerFactory,
            IQueueClient queueClient,
            Func<IScoped<IFhirDataStore>> fhirDataStoreFactory,
            IResourceWrapperFactory resourceWrapperFactory)
        {
            EnsureArg.IsNotNull(loggerFactory, nameof(loggerFactory));
            EnsureArg.IsNotNull(searchServiceFactory, nameof(searchServiceFactory));
            EnsureArg.IsNotNull(queueClient, nameof(queueClient));
            EnsureArg.IsNotNull(fhirDataStoreFactory, nameof(fhirDataStoreFactory));
            EnsureArg.IsNotNull(resourceWrapperFactory, nameof(resourceWrapperFactory));

            _logger = loggerFactory.CreateLogger<ReindexOrchestratorJob>();
            _searchServiceFactory = searchServiceFactory;
            _queueClient = queueClient;
            _fhirDataStoreFactory = fhirDataStoreFactory;
            _resourceWrapperFactory = resourceWrapperFactory;
        }

        public async Task<string> ExecuteAsync(JobInfo jobInfo, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(jobInfo, nameof(jobInfo));

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

            if (searchResultReindex != null)
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

            if (string.IsNullOrEmpty(_reindexProcessingJobDefinition.ResourceTypeSearchParameterHashMap))
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
                    _logger.LogError(ex, "Error running reindex query for resource type {ResourceType}, job id: {Id}, group id: {GroupId}.", _reindexProcessingJobDefinition.ResourceType, _jobInfo.Id, _jobInfo.GroupId);
                    _reindexProcessingJobResult.Error = reindexJobException.Message + " : " + ex.Message;
                    LogReindexProcessingJobErrorMessage();

                    throw reindexJobException;
                }
            }
        }

        private void LogReindexProcessingJobErrorMessage()
        {
            var ser = JsonConvert.SerializeObject(_reindexProcessingJobDefinition);
            var result = JsonConvert.SerializeObject(_reindexProcessingJobResult);
            _logger.LogInformation($"ReindexProcessingJob Error: Current ReindexJobRecord: {ser}, job id: {_jobInfo.Id}, group id: {_jobInfo.GroupId}. ReindexProcessing Job Result: {result}.");
        }

        private async Task ProcessQueryAsync(CancellationToken cancellationToken)
        {
            long resourceCount = 0;
            try
            {
                SearchResult result = await _timeoutRetries.ExecuteAsync(async () => await GetResourcesToReindexAsync(_reindexProcessingJobDefinition.ResourceCount, cancellationToken));
                if (result == null)
                {
                    throw new OperationFailedException($"Search service returned null search result.", HttpStatusCode.InternalServerError);
                }

                resourceCount += result.TotalCount ?? 0;
                _reindexProcessingJobResult.SearchParameterUrls = _reindexProcessingJobDefinition?.SearchParameterUrls;
                _jobInfo.Data = resourceCount;

                // Check if we need to create a child job to continue processing
                if (ShouldCreateContinuationJob(result))
                {
                    await CreateAndEnqueueContinuationJobAsync(result, cancellationToken);
                }

                var dictionary = new Dictionary<string, string>
                {
                    { _reindexProcessingJobDefinition.ResourceType, _reindexProcessingJobDefinition.ResourceTypeSearchParameterHashMap },
                };

                await _timeoutRetries.ExecuteAsync(async () => await ProcessSearchResultsAsync(result, dictionary, (int)_reindexProcessingJobDefinition.MaximumNumberOfResourcesPerWrite, cancellationToken));

                if (!cancellationToken.IsCancellationRequested)
                {
                    _reindexProcessingJobResult.SucceededResourceCount += (long)result?.Results.Count();
                    _logger.LogInformation("Reindex processing job complete. Current number of resources indexed by this job: {Progress}, job id: {Id}", _reindexProcessingJobResult.SucceededResourceCount, _jobInfo.Id);
                }
            }
            catch (SqlException sqlEx)
            {
                LogReindexProcessingJobErrorMessage();

                // Check if this is a timeout exception
                if (sqlEx.IsExecutionTimeout())
                {
                    // Increment the timeout count in the job result
                    _reindexProcessingJobResult.TimeoutCount = (_reindexProcessingJobResult.TimeoutCount ?? 0) + 1;

                    // If we've hit max retries for timeouts, fail the job
                    if (_reindexProcessingJobResult.TimeoutCount >= MaxTimeoutRetries)
                    {
                        _logger.LogError("Maximum SQL timeout retries ({MaxRetries}) reached. Job id: {Id}, group id: {GroupId}.", MaxTimeoutRetries, _jobInfo.Id, _jobInfo.GroupId);

                        _reindexProcessingJobResult.Error = $"SQL Error: {sqlEx.Message}";
                        _reindexProcessingJobResult.FailedResourceCount = resourceCount;

                        throw new OperationFailedException($"Maximum SQL timeout retries reached: {sqlEx.Message}", HttpStatusCode.InternalServerError);
                    }

                    // Otherwise log a warning and return without throwing (allowing a retry)
                    _logger.LogWarning("SQL timeout occurred during reindex processing - retry {RetryCount} of {MaxRetries}. Job id: {Id}", _reindexProcessingJobResult.TimeoutCount, MaxTimeoutRetries, _jobInfo.Id);
                    _jobInfo.Status = JobStatus.Created;

                    return;
                }

                // For non-timeout SQL errors, throw an exception to fail the job
                _logger.LogError(sqlEx, "SQL error occurred during reindex processing. Job id: {Id}, group id: {GroupId}.", _jobInfo.Id, _jobInfo.GroupId);
                _reindexProcessingJobResult.Error = $"SQL Error: {sqlEx.Message}";
                _reindexProcessingJobResult.FailedResourceCount = resourceCount;

                throw new OperationFailedException($"SQL Error occurred during reindex processing: {sqlEx.Message}", HttpStatusCode.InternalServerError);
            }
            catch (FhirException ex)
            {
                _logger.LogError(ex, "Reindex processing job error occurred. Job id: {Id}. Is FhirException: true", _jobInfo.Id);
                LogReindexProcessingJobErrorMessage();
                _reindexProcessingJobResult.Error = ex.Message;
                _reindexProcessingJobResult.FailedResourceCount = resourceCount;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Reindex processing job error occurred. Job id: {Id}. Is FhirException: false", _jobInfo.Id);
                LogReindexProcessingJobErrorMessage();
                _reindexProcessingJobResult.Error = ex.Message;
                _reindexProcessingJobResult.FailedResourceCount = resourceCount;
            }
        }

        private async Task<IReadOnlyList<long>> EnqueueChildQueryProcessingJobAsync(ReindexProcessingJobDefinition childJobDefinition, CancellationToken cancellationToken)
        {
            var definitions = new List<string>
            {
                // Finish mapping to processing job
                JsonConvert.SerializeObject(childJobDefinition),
            };

            try
            {
                var jobIds = (await _queueClient.EnqueueAsync((byte)QueueType.Reindex, definitions.ToArray(), childJobDefinition.GroupId, false, cancellationToken))
                    .Select(_ => _.Id)
                    .OrderBy(_ => _)
                    .ToList();
                return jobIds;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to enqueue child jobs.");
                _reindexProcessingJobResult.Error = ex.Message;

                // Replace RetriableJobException with in-place retry logic
                int retryCount = 3;
                for (int i = 0; i < retryCount; i++)
                {
                    try
                    {
                        var retryJobIds = (await _queueClient.EnqueueAsync((byte)QueueType.Reindex, definitions.ToArray(), childJobDefinition.GroupId, false, cancellationToken))
                            .Select(_ => _.Id)
                            .OrderBy(_ => _)
                            .ToList();
                        return retryJobIds;
                    }
                    catch
                    {
                        if (i == retryCount - 1)
                        {
                            throw; // Rethrow the exception after exhausting retries
                        }
                    }
                }

                throw; // This line should never be reached
            }
        }

        /// <summary>
        /// For each result in a batch of resources this will extract new search params
        /// Then compare those to the old values to determine if an update is needed
        /// Needed updates will be committed in a batch
        /// </summary>
        /// <param name="results">The resource batch to process</param>
        /// <param name="resourceTypeSearchParameterHashMap">Map of resource type to current hash value of the search parameters for that resource type</param>
        /// <param name="batchSize">The number of resources to reindex at a time (e.g. 1000)</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>A Task</returns>
        public async Task ProcessSearchResultsAsync(SearchResult results, IReadOnlyDictionary<string, string> resourceTypeSearchParameterHashMap, int batchSize, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(results, nameof(results));

            var updateSearchIndices = new List<ResourceWrapper>();

            // This should never happen, but in case it does, we will set a low default to ensure we don't get stuck in loop
            if (batchSize == 0)
            {
                batchSize = 500;
            }

            foreach (var entry in results.Results)
            {
                if (!resourceTypeSearchParameterHashMap.TryGetValue(entry.Resource.ResourceTypeName, out string searchParamHash))
                {
                    searchParamHash = string.Empty;
                }

                entry.Resource.SearchParameterHash = searchParamHash;
                _resourceWrapperFactory.Update(entry.Resource);
                updateSearchIndices.Add(entry.Resource);

                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
            }

            using (IScoped<IFhirDataStore> store = _fhirDataStoreFactory())
            {
                for (int i = 0; i < updateSearchIndices.Count; i += batchSize)
                {
                    var batch = updateSearchIndices.GetRange(i, Math.Min(batchSize, updateSearchIndices.Count - i));
                    await store.Value.BulkUpdateSearchParameterIndicesAsync(batch, cancellationToken);

                    if (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }
                }
            }
        }

        /// <summary>
        /// Determines whether a continuation job should be created based on the search results.
        /// </summary>
        /// <param name="result">The search result from the current processing batch</param>
        /// <returns>True if a continuation job is needed, false otherwise</returns>
        private bool ShouldCreateContinuationJob(SearchResult result)
        {
            return result.MaxResourceSurrogateId > 0 &&
                   result.MaxResourceSurrogateId < _reindexProcessingJobDefinition.ResourceCount.EndResourceSurrogateId;
        }

        /// <summary>
        /// Creates and enqueues a continuation job to process the remaining resources in the surrogate ID range.
        /// </summary>
        /// <param name="result">The search result containing the last processed resource information</param>
        /// <param name="cancellationToken">Cancellation token</param>
        private async Task CreateAndEnqueueContinuationJobAsync(SearchResult result, CancellationToken cancellationToken)
        {
            var originalEndId = _reindexProcessingJobDefinition.ResourceCount.EndResourceSurrogateId;
            var originalCount = _reindexProcessingJobDefinition.ResourceCount.Count;
            var processedCount = result.Results?.Count() ?? 0;

            // Create a new ResourceCount for the child job
            var childResourceCount = new SearchResultReindex(Math.Max(0, originalCount - processedCount))
            {
                StartResourceSurrogateId = result.MaxResourceSurrogateId + 1,
                EndResourceSurrogateId = originalEndId,
                ContinuationToken = result.ContinuationToken,
            };

            // Create a new job definition for the child
            var childJobDefinition = new ReindexProcessingJobDefinition()
            {
                TypeId = _reindexProcessingJobDefinition.TypeId,
                GroupId = _reindexProcessingJobDefinition.GroupId,
                ResourceType = _reindexProcessingJobDefinition.ResourceType,
                MaximumNumberOfResourcesPerQuery = _reindexProcessingJobDefinition.MaximumNumberOfResourcesPerQuery,
                MaximumNumberOfResourcesPerWrite = _reindexProcessingJobDefinition.MaximumNumberOfResourcesPerWrite,
                ResourceTypeSearchParameterHashMap = _reindexProcessingJobDefinition.ResourceTypeSearchParameterHashMap,
                SearchParameterUrls = _reindexProcessingJobDefinition.SearchParameterUrls,
                ResourceCount = childResourceCount,
            };

            var generatedJobId = await EnqueueChildQueryProcessingJobAsync(childJobDefinition, cancellationToken);

            _logger.LogInformation(
                "Reindex processing job created a child query to finish processing, job id: {JobId}, group id: {GroupId}. New job id: {NewJobId}. Range: {StartId}-{EndId}",
                _jobInfo.Id,
                _jobInfo.GroupId,
                generatedJobId[0],
                childResourceCount.StartResourceSurrogateId,
                childResourceCount.EndResourceSurrogateId);
        }
    }
}
