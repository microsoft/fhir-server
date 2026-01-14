// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.ClientModel;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Operations.Reindex.Models;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.JobManagement;
using Newtonsoft.Json;
using Polly;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Reindex
{
    [JobTypeId((int)JobType.ReindexProcessing)]
    public class ReindexProcessingJob : IJob
    {
        private static readonly AsyncPolicy _timeoutRetries = Policy
            .Handle<SqlException>(ex => ex.IsExecutionTimeout())
            .WaitAndRetryAsync(MaxTimeoutRetries, _ => TimeSpan.FromMilliseconds(RandomNumberGenerator.GetInt32(1000, 5000)));

        private readonly Func<IScoped<ISearchService>> _searchServiceFactory;
        private readonly IResourceWrapperFactory _resourceWrapperFactory;
        private readonly Func<IScoped<IFhirDataStore>> _fhirDataStoreFactory;
        private readonly ISearchParameterOperations _searchParameterOperations;
        private readonly ILogger<ReindexProcessingJob> _logger;

        private JobInfo _jobInfo;
        private ReindexProcessingJobResult _reindexProcessingJobResult;
        private ReindexProcessingJobDefinition _reindexProcessingJobDefinition;
        private const int MaxTimeoutRetries = 3;

        /// <summary>
        /// Internal batch size for fetching resources to prevent OutOfMemoryException when processing large batches.
        /// Even if MaximumNumberOfResourcesPerQuery is set to 10000, we fetch at most this many resources at a time
        /// to avoid loading too many large resources into memory simultaneously.
        /// </summary>
        private const int MemorySafeBatchSize = 2000;

        public ReindexProcessingJob(
            Func<IScoped<ISearchService>> searchServiceFactory,
            Func<IScoped<IFhirDataStore>> fhirDataStoreFactory,
            IResourceWrapperFactory resourceWrapperFactory,
            ISearchParameterOperations searchParameterOperations,
            ILogger<ReindexProcessingJob> logger)
        {
            EnsureArg.IsNotNull(searchServiceFactory, nameof(searchServiceFactory));
            EnsureArg.IsNotNull(fhirDataStoreFactory, nameof(fhirDataStoreFactory));
            EnsureArg.IsNotNull(resourceWrapperFactory, nameof(resourceWrapperFactory));
            EnsureArg.IsNotNull(searchParameterOperations, nameof(searchParameterOperations));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _searchServiceFactory = searchServiceFactory;
            _fhirDataStoreFactory = fhirDataStoreFactory;
            _resourceWrapperFactory = resourceWrapperFactory;
            _searchParameterOperations = searchParameterOperations;
            _logger = logger;
        }

        public async Task<string> ExecuteAsync(JobInfo jobInfo, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(jobInfo, nameof(jobInfo));

            _jobInfo = jobInfo;
            _reindexProcessingJobDefinition = DeserializeJobDefinition(jobInfo);
            _reindexProcessingJobResult = new ReindexProcessingJobResult();

            await ProcessQueryAsync(cancellationToken);

            return JsonConvert.SerializeObject(_reindexProcessingJobResult);
        }

        private async Task<SearchResult> GetResourcesToReindexAsync(SearchResultReindex searchResultReindex, CancellationToken cancellationToken, int? maxBatchSize = null)
        {
            string searchParameterHash = _reindexProcessingJobDefinition.ResourceTypeSearchParameterHashMap;
            searchParameterHash ??= string.Empty;

            var queryParametersList = new List<Tuple<string, string>>()
            {
                Tuple.Create(KnownQueryParameterNames.Type, _reindexProcessingJobDefinition.ResourceType),
            };

            if (searchResultReindex != null)
            {
                // If we have SurrogateId range, we simply use those and ignore search parameter hash
                // Otherwise, it's cosmos DB and we must use it and ensure we pass MaximumNumberOfResourcesPerQuery so we get expected count returned.
                if (searchResultReindex.StartResourceSurrogateId > 0 && searchResultReindex.EndResourceSurrogateId > 0)
                {
                    queryParametersList.Add(Tuple.Create(KnownQueryParameterNames.IgnoreSearchParamHash, "true"));

                    // Always use the StartResourceSurrogateId for the start of the range
                    // and the ResourceCount.EndResourceSurrogateId for the end. The sql will determine
                    // how many resources to actually return based on the configured maximumNumberOfResourcesPerQuery.
                    // When this function returns, it knows what the next starting value to use in
                    // searching for the next block of results and will use that as the queryStatus starting point
                    queryParametersList.AddRange(new[]
                    {
                        // This EndResourceSurrogateId is only needed because of the way the sql is written. It is not accurate initially.
                        Tuple.Create(KnownQueryParameterNames.StartSurrogateId, searchResultReindex.StartResourceSurrogateId.ToString()),
                        Tuple.Create(KnownQueryParameterNames.EndSurrogateId, searchResultReindex.EndResourceSurrogateId.ToString()),
                        Tuple.Create(KnownQueryParameterNames.GlobalEndSurrogateId, "0"),
                    });

                    // When maxBatchSize is provided, add a count parameter to limit resources fetched per query
                    // This prevents OutOfMemoryException when processing large batches with large resources
                    if (maxBatchSize.HasValue)
                    {
                        queryParametersList.Add(Tuple.Create(KnownQueryParameterNames.Count, maxBatchSize.Value.ToString()));
                    }
                }
                else
                {
                    queryParametersList.Add(Tuple.Create(KnownQueryParameterNames.Count, _reindexProcessingJobDefinition.MaximumNumberOfResourcesPerQuery.ToString()));
                }

                if (searchResultReindex.ContinuationToken != null)
                {
                    queryParametersList.Add(Tuple.Create(KnownQueryParameterNames.ContinuationToken, searchResultReindex.ContinuationToken));
                }
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
                    var reindexJobException = new ReindexProcessingJobSoftException(message, ex);
                    _logger.LogJobError(ex, _jobInfo, "Error running reindex query for resource type {ResourceType}.", _reindexProcessingJobDefinition.ResourceType);
                    LogReindexProcessingJobErrorMessage();

                    throw reindexJobException;
                }
            }
        }

        private void LogReindexProcessingJobErrorMessage()
        {
            var ser = JsonConvert.SerializeObject(_reindexProcessingJobDefinition);
            var result = JsonConvert.SerializeObject(_reindexProcessingJobResult);
            _logger.LogJobInformation(_jobInfo, "ReindexProcessingJob Error: Current ReindexJobRecord: {JobDefinition}. ReindexProcessing Job Result: {JobResult}.", ser, result);
        }

        private async Task ProcessQueryAsync(CancellationToken cancellationToken)
        {
            if (_reindexProcessingJobDefinition == null)
            {
                throw new InvalidOperationException("_reindexProcessingJobDefinition cannot be null during processing.");
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            long resourceCount = 0;
            try
            {
                _reindexProcessingJobResult.SearchParameterUrls = _reindexProcessingJobDefinition.SearchParameterUrls;

                var dictionary = new Dictionary<string, string>
                {
                    { _reindexProcessingJobDefinition.ResourceType, _reindexProcessingJobDefinition.ResourceTypeSearchParameterHashMap },
                };

                // Determine if we're using SQL Server path (surrogate ID range) or Cosmos DB path (continuation tokens)
                bool useSurrogateIdRange = _reindexProcessingJobDefinition.ResourceCount != null
                    && _reindexProcessingJobDefinition.ResourceCount.StartResourceSurrogateId > 0
                    && _reindexProcessingJobDefinition.ResourceCount.EndResourceSurrogateId > 0;

                if (useSurrogateIdRange)
                {
                    // SQL Server path: Fetch resources in memory-safe batches using surrogate ID ranges
                    // to prevent OutOfMemoryException when processing large batches with large resources
                    await ProcessWithSurrogateIdBatchingAsync(dictionary, cancellationToken);
                }
                else
                {
                    // Cosmos DB path: Use continuation tokens for pagination
                    await ProcessWithContinuationTokensAsync(dictionary, cancellationToken);
                }

                if (!cancellationToken.IsCancellationRequested)
                {
                    _logger.LogJobInformation(_jobInfo, "Reindex processing job complete. Total number of resources indexed by this job: {Progress}.", _reindexProcessingJobResult.SucceededResourceCount);
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
                        _logger.LogJobError(_jobInfo, "Maximum SQL timeout retries ({MaxRetries}) reached.", MaxTimeoutRetries);

                        _reindexProcessingJobResult.Error = $"SQL Error: {sqlEx.Message}";
                        _reindexProcessingJobResult.FailedResourceCount = resourceCount;

                        throw new OperationFailedException($"Maximum SQL timeout retries reached: {sqlEx.Message}", HttpStatusCode.InternalServerError);
                    }

                    // Otherwise log a warning and return without throwing (allowing a retry)
                    _logger.LogJobWarning(_jobInfo, "SQL timeout occurred during reindex processing - retry {RetryCount} of {MaxRetries}.", _reindexProcessingJobResult.TimeoutCount, MaxTimeoutRetries);
                    _jobInfo.Status = JobStatus.Created;

                    return;
                }

                // For non-timeout SQL errors, throw an exception to fail the job
                _logger.LogJobError(sqlEx, _jobInfo, "SQL error occurred during reindex processing.");
                _reindexProcessingJobResult.Error = $"SQL Error: {sqlEx.Message}";
                _reindexProcessingJobResult.FailedResourceCount = resourceCount;

                throw new OperationFailedException($"SQL Error occurred during reindex processing: {sqlEx.Message}", HttpStatusCode.InternalServerError);
            }
            catch (FhirException ex)
            {
                _logger.LogJobError(ex, _jobInfo, "Reindex processing job error occurred. Is FhirException: 'true'.");
                LogReindexProcessingJobErrorMessage();
                _reindexProcessingJobResult.Error = ex.Message;
                _reindexProcessingJobResult.FailedResourceCount = _reindexProcessingJobDefinition.ResourceCount.Count - _reindexProcessingJobResult.SucceededResourceCount;
            }
            catch (Exception ex)
            {
                _logger.LogJobError(ex, _jobInfo, "Reindex processing job error occurred. Is FhirException: 'false'.");
                LogReindexProcessingJobErrorMessage();
                _reindexProcessingJobResult.Error = ex.Message;
                _reindexProcessingJobResult.FailedResourceCount = _reindexProcessingJobDefinition.ResourceCount.Count - _reindexProcessingJobResult.SucceededResourceCount;
            }
        }

        /// <summary>
        /// Processes resources using surrogate ID batching for SQL Server.
        /// Fetches resources in smaller memory-safe batches to prevent OutOfMemoryException.
        /// </summary>
        private async Task ProcessWithSurrogateIdBatchingAsync(Dictionary<string, string> dictionary, CancellationToken cancellationToken)
        {
            long currentStartId = _reindexProcessingJobDefinition.ResourceCount.StartResourceSurrogateId;
            long globalEndId = _reindexProcessingJobDefinition.ResourceCount.EndResourceSurrogateId;

            _logger.LogJobInformation(
                _jobInfo,
                "Starting reindex with surrogate ID batching. StartId={StartId}, EndId={EndId}, MemorySafeBatchSize={BatchSize}",
                currentStartId,
                globalEndId,
                MemorySafeBatchSize);

            while (currentStartId <= globalEndId && !cancellationToken.IsCancellationRequested)
            {
                // Create a search request for the current batch
                var batchSearchResult = new SearchResultReindex(_reindexProcessingJobDefinition.ResourceCount.Count)
                {
                    StartResourceSurrogateId = currentStartId,
                    EndResourceSurrogateId = globalEndId,
                };

                SearchResult result = await _timeoutRetries.ExecuteAsync(
                    async () => await GetResourcesToReindexAsync(batchSearchResult, cancellationToken, MemorySafeBatchSize));

                if (result == null)
                {
                    throw new OperationFailedException("Search service returned null search result.", HttpStatusCode.InternalServerError);
                }

                int batchResourceCount = result.Results?.Count() ?? 0;
                if (batchResourceCount == 0)
                {
                    // No more resources in this range
                    _logger.LogJobInformation(_jobInfo, "No more resources found in surrogate ID range. CurrentStartId={CurrentStartId}, GlobalEndId={GlobalEndId}", currentStartId, globalEndId);
                    break;
                }

                // Process the current batch
                await _timeoutRetries.ExecuteAsync(
                    async () => await ProcessSearchResultsAsync(result, dictionary, (int)_reindexProcessingJobDefinition.MaximumNumberOfResourcesPerWrite, cancellationToken));

                _reindexProcessingJobResult.SucceededResourceCount += batchResourceCount;
                _jobInfo.Data = _reindexProcessingJobResult.SucceededResourceCount;

                _logger.LogJobInformation(
                    _jobInfo,
                    "Reindex batch complete. BatchSize={BatchSize}, CurrentStartId={CurrentStartId}, MaxResourceSurrogateId={MaxId}, TotalProcessed={TotalProcessed}",
                    batchResourceCount,
                    currentStartId,
                    result.MaxResourceSurrogateId,
                    _reindexProcessingJobResult.SucceededResourceCount);

                // Move to the next batch - start from the resource after the last one we processed
                if (result.MaxResourceSurrogateId > 0)
                {
                    currentStartId = result.MaxResourceSurrogateId + 1;
                }
                else
                {
                    // Fallback: if MaxResourceSurrogateId is not set, we're done
                    break;
                }
            }
        }

        /// <summary>
        /// Processes resources using continuation tokens for Cosmos DB.
        /// </summary>
        private async Task ProcessWithContinuationTokensAsync(Dictionary<string, string> dictionary, CancellationToken cancellationToken)
        {
            SearchResult result = await _timeoutRetries.ExecuteAsync(
                async () => await GetResourcesToReindexAsync(_reindexProcessingJobDefinition.ResourceCount, cancellationToken));

            if (result == null)
            {
                throw new OperationFailedException("Search service returned null search result.", HttpStatusCode.InternalServerError);
            }

            long resourceCount = result.TotalCount ?? result.Results?.Count() ?? 0;
            _jobInfo.Data = resourceCount;

            if (resourceCount > _reindexProcessingJobDefinition.MaximumNumberOfResourcesPerQuery)
            {
                _logger.LogJobWarning(
                    _jobInfo,
                    "Reindex: number of resources is higher than the original limit. Current count: {CurrentCount}. Original limit: {OriginalLimit}",
                    resourceCount,
                    _reindexProcessingJobDefinition.MaximumNumberOfResourcesPerQuery);
            }

            // Process results in a loop to handle continuation tokens
            do
            {
                await _timeoutRetries.ExecuteAsync(
                    async () => await ProcessSearchResultsAsync(result, dictionary, (int)_reindexProcessingJobDefinition.MaximumNumberOfResourcesPerWrite, cancellationToken));

                resourceCount += result.TotalCount ?? result.Results?.Count() ?? 0;
                _jobInfo.Data = resourceCount;
                _reindexProcessingJobResult.SucceededResourceCount += result?.Results.Count() ?? 0;
                _jobInfo.Data = _reindexProcessingJobResult.SucceededResourceCount;
                _logger.LogJobInformation(_jobInfo, "Reindex processing batch complete. Current number of resources indexed by this job: {Progress}.", _reindexProcessingJobResult.SucceededResourceCount);

                // Check if there's a continuation token to fetch more results
                if (!string.IsNullOrEmpty(result.ContinuationToken) && !cancellationToken.IsCancellationRequested)
                {
                    _logger.LogJobInformation(_jobInfo, "Continuation token found. Fetching next batch of resources for reindexing.");

                    // Clear the previous continuation token first to avoid conflicts
                    _reindexProcessingJobDefinition.ResourceCount.ContinuationToken = null;

                    // Create a new SearchResultReindex with the continuation token for the next query
                    var nextSearchResultReindex = new SearchResultReindex(_reindexProcessingJobDefinition.ResourceCount.Count)
                    {
                        StartResourceSurrogateId = _reindexProcessingJobDefinition.ResourceCount.StartResourceSurrogateId,
                        EndResourceSurrogateId = _reindexProcessingJobDefinition.ResourceCount.EndResourceSurrogateId,
                        ContinuationToken = Convert.ToBase64String(Encoding.UTF8.GetBytes(result.ContinuationToken)),
                    };

                    // Fetch the next batch of results
                    result = await _timeoutRetries.ExecuteAsync(
                        async () => await GetResourcesToReindexAsync(nextSearchResultReindex, cancellationToken));

                    if (result == null)
                    {
                        throw new OperationFailedException("Search service returned null search result during continuation.", HttpStatusCode.InternalServerError);
                    }
                }
                else
                {
                    // No more continuation token, exit the loop
                    result = null;
                }
            }
            while (result != null && !cancellationToken.IsCancellationRequested);
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

        private static ReindexProcessingJobDefinition DeserializeJobDefinition(JobInfo jobInfo)
        {
            return JsonConvert.DeserializeObject<ReindexProcessingJobDefinition>(jobInfo.Definition);
        }
    }
}
