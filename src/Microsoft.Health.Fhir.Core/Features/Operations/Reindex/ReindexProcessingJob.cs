// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
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

            var totalStopwatch = Stopwatch.StartNew();
            _logger.LogJobInformation(jobInfo, "ReindexProcessingJob started for JobId={JobId}", jobInfo.Id);

            _jobInfo = jobInfo;
            _reindexProcessingJobDefinition = DeserializeJobDefinition(jobInfo);
            _reindexProcessingJobResult = new ReindexProcessingJobResult();

            var validationStopwatch = Stopwatch.StartNew();
            ValidateSearchParametersHash(_jobInfo, _reindexProcessingJobDefinition, cancellationToken);
            validationStopwatch.Stop();
            _logger.LogJobInformation(jobInfo, "Validation completed in {ElapsedMs}ms", validationStopwatch.ElapsedMilliseconds);

            await ProcessQueryAsync(cancellationToken);

            totalStopwatch.Stop();
            _logger.LogJobInformation(
                jobInfo,
                "ReindexProcessingJob completed for JobId={JobId}. Total time: {TotalMs}ms, Resources processed: {ResourceCount}",
                jobInfo.Id,
                totalStopwatch.ElapsedMilliseconds,
                _reindexProcessingJobResult.SucceededResourceCount);

            return JsonConvert.SerializeObject(_reindexProcessingJobResult);
        }

        private void ValidateSearchParametersHash(JobInfo jobInfo, ReindexProcessingJobDefinition jobDefinition, CancellationToken cancellationToken)
        {
            string currentResourceTypeHash = _searchParameterOperations.GetResourceTypeSearchParameterHashMap(jobDefinition.ResourceType);

            // If the hash is different, we need to fail job as this means something has changed unexpectedly.
            // This ensures that the processing job always uses the latest search parameters and we do not complete job incorrectly.

            if (string.IsNullOrEmpty(currentResourceTypeHash) || !string.Equals(currentResourceTypeHash, jobDefinition.ResourceTypeSearchParameterHashMap, StringComparison.Ordinal))
            {
                _logger.LogJobError(
                    jobInfo,
                    "Search parameters for resource type {ResourceType} have changed since the Reindex Job was created. Job definition hash: '{CurrentHash}', In-memory hash: '{JobHash}'.",
                    jobDefinition.ResourceType,
                    jobDefinition.ResourceTypeSearchParameterHashMap,
                    currentResourceTypeHash);

                string message = "Search Parameter hash does not match. Resubmit reindex job to try again.";

                // Create error object to provide structured error information
                var errorObject = new
                {
                    message = message,
                    resourceType = jobDefinition.ResourceType,
                    jobDefinitionHash = jobDefinition.ResourceTypeSearchParameterHashMap,
                    currentHash = currentResourceTypeHash,
                    jobId = jobInfo.Id,
                    groupId = jobInfo.GroupId,
                };

                throw new ReindexProcessingJobSoftException(message, errorObject, isCustomerCaused: true);
            }
        }

        private async Task<SearchResult> GetResourcesToReindexAsync(SearchResultReindex searchResultReindex, CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();
            _logger.LogJobInformation(
                _jobInfo,
                "Starting search query for ResourceType={ResourceType}, StartId={StartId}, EndId={EndId}",
                _reindexProcessingJobDefinition.ResourceType,
                searchResultReindex?.StartResourceSurrogateId,
                searchResultReindex?.EndResourceSurrogateId);

            var queryParametersList = new List<Tuple<string, string>>()
            {
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
                    var searchStopwatch = Stopwatch.StartNew();
                    var result = await searchService.Value.SearchForReindexAsync(queryParametersList, searchParameterHash, false, cancellationToken, true);
                    searchStopwatch.Stop();

                    stopwatch.Stop();
                    _logger.LogJobInformation(
                        _jobInfo,
                        "Search query completed in {ElapsedMs}ms (SQL query: {SqlMs}ms). Results: {ResultCount}, TotalCount: {TotalCount}",
                        stopwatch.ElapsedMilliseconds,
                        searchStopwatch.ElapsedMilliseconds,
                        result?.Results?.Count() ?? 0,
                        result?.TotalCount ?? 0);

                    return result;
                }
                catch (Exception ex)
                {
                    stopwatch.Stop();
                    var message = $"Error running reindex query for resource type {_reindexProcessingJobDefinition.ResourceType}.";
                    var reindexJobException = new ReindexProcessingJobSoftException(message, ex);
                    _logger.LogJobError(ex, _jobInfo, "Search query failed after {ElapsedMs}ms for resource type {ResourceType}.", stopwatch.ElapsedMilliseconds, _reindexProcessingJobDefinition.ResourceType);
                    LogReindexProcessingJobErrorMessage();

                    throw reindexJobException;
                }
            }
        }

        private void LogReindexProcessingJobErrorMessage()
        {
            var ser = JsonConvert.SerializeObject(_reindexProcessingJobDefinition);
            var result = JsonConvert.SerializeObject(_reindexProcessingJobResult);
            _logger.LogJobInformation(_jobInfo, "ReindexProcessingJob Error: Current ReindexJobRecord: {ReindexJobRecord}. ReindexProcessing Job Result: {Result}.", ser, result);
        }

        private async Task ProcessQueryAsync(CancellationToken cancellationToken)
        {
            if (_reindexProcessingJobDefinition == null)
            {
                throw new InvalidOperationException("_reindexProcessingJobDefinition cannot be null during processing.");
            }

            var totalStopwatch = Stopwatch.StartNew();
            long resourceCount = 0;
            try
            {
                var searchStopwatch = Stopwatch.StartNew();
                _logger.LogJobInformation(_jobInfo, "Starting GetResourcesToReindexAsync...");

                SearchResult result = await _timeoutRetries.ExecuteAsync(async () => await GetResourcesToReindexAsync(_reindexProcessingJobDefinition.ResourceCount, cancellationToken));
                searchStopwatch.Stop();
                _logger.LogJobInformation(_jobInfo, "GetResourcesToReindexAsync completed in {ElapsedMs}ms", searchStopwatch.ElapsedMilliseconds);

                if (result == null)
                {
                    throw new OperationFailedException($"Search service returned null search result.", HttpStatusCode.InternalServerError);
                }

                resourceCount = result.TotalCount ?? 0;
                _reindexProcessingJobResult.SearchParameterUrls = _reindexProcessingJobDefinition.SearchParameterUrls;
                _jobInfo.Data = resourceCount;

                if (resourceCount > _reindexProcessingJobDefinition.MaximumNumberOfResourcesPerQuery)
                {
                    _logger.LogJobWarning(
                        _jobInfo,
                        "Reindex: number of resources is higher than the original limit. Current count: {CurrentCount}. Original limit: {OriginalLimit}",
                        resourceCount,
                        _reindexProcessingJobDefinition.MaximumNumberOfResourcesPerQuery);
                }

                var dictionary = new Dictionary<string, string>
                {
                    { _reindexProcessingJobDefinition.ResourceType, _reindexProcessingJobDefinition.ResourceTypeSearchParameterHashMap },
                };

                var processStopwatch = Stopwatch.StartNew();
                _logger.LogJobInformation(
                    _jobInfo,
                    "Starting ProcessSearchResultsAsync for {ResourceCount} resources with batch size {BatchSize}...",
                    result.Results.Count(),
                    _reindexProcessingJobDefinition.MaximumNumberOfResourcesPerWrite);

                await _timeoutRetries.ExecuteAsync(async () => await ProcessSearchResultsAsync(result, dictionary, (int)_reindexProcessingJobDefinition.MaximumNumberOfResourcesPerWrite, cancellationToken));
                processStopwatch.Stop();

                _logger.LogJobInformation(
                    _jobInfo,
                    "ProcessSearchResultsAsync completed in {ElapsedMs}ms ({ResourcesPerSecond} resources/sec)",
                    processStopwatch.ElapsedMilliseconds,
                    processStopwatch.ElapsedMilliseconds > 0 ? (result.Results.Count() * 1000.0 / processStopwatch.ElapsedMilliseconds).ToString("F2") : "N/A");

                if (!cancellationToken.IsCancellationRequested)
                {
                    _reindexProcessingJobResult.SucceededResourceCount += (long)result?.Results.Count();
                    totalStopwatch.Stop();
                    _logger.LogJobInformation(
                        _jobInfo,
                        "ProcessQueryAsync completed successfully. Total time: {TotalMs}ms, Search: {SearchMs}ms, Processing: {ProcessMs}ms, Resources indexed: {ResourceCount}",
                        totalStopwatch.ElapsedMilliseconds,
                        searchStopwatch.ElapsedMilliseconds,
                        processStopwatch.ElapsedMilliseconds,
                        _reindexProcessingJobResult.SucceededResourceCount);
                }
            }
            catch (OperationCanceledException)
            {
                totalStopwatch.Stop();
                _logger.LogJobInformation(_jobInfo, "ProcessQueryAsync cancelled after {ElapsedMs}ms", totalStopwatch.ElapsedMilliseconds);
                _jobInfo.Status = JobStatus.Cancelled;
            }
            catch (SqlException sqlEx)
            {
                totalStopwatch.Stop();
                _logger.LogJobError(sqlEx, _jobInfo, "ProcessQueryAsync failed after {ElapsedMs}ms with SQL exception", totalStopwatch.ElapsedMilliseconds);
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
                totalStopwatch.Stop();
                _logger.LogJobError(ex, _jobInfo, "ProcessQueryAsync failed after {ElapsedMs}ms with FhirException", totalStopwatch.ElapsedMilliseconds);
                LogReindexProcessingJobErrorMessage();
                _reindexProcessingJobResult.Error = ex.Message;
                _reindexProcessingJobResult.FailedResourceCount = resourceCount;
            }
            catch (Exception ex)
            {
                totalStopwatch.Stop();
                _logger.LogJobError(ex, _jobInfo, "ProcessQueryAsync failed after {ElapsedMs}ms with unexpected exception", totalStopwatch.ElapsedMilliseconds);
                LogReindexProcessingJobErrorMessage();
                _reindexProcessingJobResult.Error = ex.Message;
                _reindexProcessingJobResult.FailedResourceCount = resourceCount;
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

            var totalStopwatch = Stopwatch.StartNew();

            // This should never happen, but in case it does, we will set a low default to ensure we don't get stuck in loop
            if (batchSize == 0)
            {
                batchSize = 500;
            }

            var extractionStopwatch = Stopwatch.StartNew();
            var resultsList = results.Results.ToList();
            _logger.LogJobInformation(
                _jobInfo,
                "Starting parallel search parameter extraction for {ResultCount} resources...",
                resultsList.Count);

            // Use ConcurrentBag for thread-safe collection
            var updateSearchIndices = new ConcurrentBag<ResourceWrapper>();

            // Parallel processing of search parameter extraction
            var parallelOptions = new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = 8,
            };

            try
            {
                Parallel.ForEach(resultsList, parallelOptions, entry =>
                {
                    if (!resourceTypeSearchParameterHashMap.TryGetValue(entry.Resource.ResourceTypeName, out string searchParamHash))
                    {
                        searchParamHash = string.Empty;
                    }

                    entry.Resource.SearchParameterHash = searchParamHash;
                    _resourceWrapperFactory.Update(entry.Resource);
                    updateSearchIndices.Add(entry.Resource);
                });
            }
            catch (OperationCanceledException)
            {
                _logger.LogJobInformation(_jobInfo, "Search parameter extraction cancelled");
                return;
            }

            extractionStopwatch.Stop();
            _logger.LogJobInformation(
                _jobInfo,
                "Search parameter extraction completed in {ElapsedMs}ms for {ResourceCount} resources ({ResourcesPerSecond} resources/sec)",
                extractionStopwatch.ElapsedMilliseconds,
                updateSearchIndices.Count,
                extractionStopwatch.ElapsedMilliseconds > 0 ? (updateSearchIndices.Count * 1000.0 / extractionStopwatch.ElapsedMilliseconds).ToString("F2") : "N/A");

            using (IScoped<IFhirDataStore> store = _fhirDataStoreFactory())
            {
                var batchCount = 0;
                var updateSearchIndicesList = updateSearchIndices.ToList();
                var totalBatches = (int)Math.Ceiling((double)updateSearchIndicesList.Count / batchSize);

                for (int i = 0; i < updateSearchIndicesList.Count; i += batchSize)
                {
                    var batch = updateSearchIndicesList.GetRange(i, Math.Min(batchSize, updateSearchIndicesList.Count - i));
                    batchCount++;

                    var batchStopwatch = Stopwatch.StartNew();
                    _logger.LogJobInformation(
                        _jobInfo,
                        "Starting batch {BatchNumber}/{TotalBatches}: updating {BatchSize} resources (indices {StartIndex}-{EndIndex})...",
                        batchCount,
                        totalBatches,
                        batch.Count,
                        i,
                        i + batch.Count - 1);

                    await store.Value.BulkUpdateSearchParameterIndicesAsync(batch, cancellationToken);
                    batchStopwatch.Stop();

                    _logger.LogJobInformation(
                        _jobInfo,
                        "Batch {BatchNumber}/{TotalBatches} completed in {ElapsedMs}ms ({ResourcesPerSecond} resources/sec)",
                        batchCount,
                        totalBatches,
                        batchStopwatch.ElapsedMilliseconds,
                        batchStopwatch.ElapsedMilliseconds > 0 ? (batch.Count * 1000.0 / batchStopwatch.ElapsedMilliseconds).ToString("F2") : "N/A");

                    if (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }
                }
            }

            totalStopwatch.Stop();
            _logger.LogJobInformation(
                _jobInfo,
                "ProcessSearchResultsAsync completed. Total time: {TotalMs}ms, Extraction: {ExtractionMs}ms, DB updates: {DbMs}ms, Resources: {ResourceCount}",
                totalStopwatch.ElapsedMilliseconds,
                extractionStopwatch.ElapsedMilliseconds,
                totalStopwatch.ElapsedMilliseconds - extractionStopwatch.ElapsedMilliseconds,
                updateSearchIndices.Count);
        }

        private static ReindexProcessingJobDefinition DeserializeJobDefinition(JobInfo jobInfo)
        {
            return JsonConvert.DeserializeObject<ReindexProcessingJobDefinition>(jobInfo.Definition);
        }
    }
}
