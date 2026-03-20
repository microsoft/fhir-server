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
using Microsoft.Health.Abstractions.Exceptions;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Operations.Reindex.Models;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
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

        private static readonly AsyncPolicy _requestRateRetries = Policy
            .Handle<RequestRateExceededException>()
            .WaitAndRetryAsync(
                MaxTimeoutRetries,
                (_, ex, _) =>
                {
                    if (ex is RequestRateExceededException rateEx && rateEx.RetryAfter.HasValue)
                    {
                        return rateEx.RetryAfter.Value;
                    }

                    return TimeSpan.FromMilliseconds(RandomNumberGenerator.GetInt32(1000, 5000));
                },
                (_, _, _, _) => Task.CompletedTask);

        /// <summary>
        /// Combined retry policy for BulkUpdateSearchParameterIndicesAsync that handles both
        /// SQL Server timeouts and Cosmos DB 429 (TooManyRequests) errors.
        /// </summary>
        private static readonly AsyncPolicy _bulkUpdateRetries = Policy.WrapAsync(_requestRateRetries, _timeoutRetries);

        private readonly Func<IScoped<ISearchService>> _searchServiceFactory;
        private readonly IResourceWrapperFactory _resourceWrapperFactory;
        private readonly Func<IScoped<IFhirDataStore>> _fhirDataStoreFactory;
        private readonly ILogger<ReindexProcessingJob> _logger;
        private readonly ISearchParameterStatusManager _searchParameterStatusManager;
        private readonly ISearchParameterOperations _searchParameterOperations;

        private JobInfo _jobInfo;
        private ReindexProcessingJobResult _reindexProcessingJobResult;
        private ReindexProcessingJobDefinition _reindexProcessingJobDefinition;
        private string _searchParameterHash;
        private const int MaxTimeoutRetries = 3;

        private const int OomReductionFactor = 10;
        private const int MinEffectiveBatchSize = 1;
        private const int MaxOomReductionsBeforeSoftFail = 3;

        /// <summary>
        /// Current effective batch size for fetching resources. Starts at the configured MaximumNumberOfResourcesPerQuery
        /// but may be reduced if OutOfMemoryException is encountered during processing.
        /// </summary>
        private int _effectiveBatchSize;

        public ReindexProcessingJob(
            Func<IScoped<ISearchService>> searchServiceFactory,
            Func<IScoped<IFhirDataStore>> fhirDataStoreFactory,
            IResourceWrapperFactory resourceWrapperFactory,
            ISearchParameterOperations searchParameterOperations,
            ISearchParameterStatusManager searchParameterStatusManager,
            ILogger<ReindexProcessingJob> logger)
        {
            EnsureArg.IsNotNull(searchServiceFactory, nameof(searchServiceFactory));
            EnsureArg.IsNotNull(fhirDataStoreFactory, nameof(fhirDataStoreFactory));
            EnsureArg.IsNotNull(resourceWrapperFactory, nameof(resourceWrapperFactory));
            EnsureArg.IsNotNull(searchParameterOperations, nameof(searchParameterOperations));
            EnsureArg.IsNotNull(searchParameterStatusManager, nameof(searchParameterStatusManager));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _searchServiceFactory = searchServiceFactory;
            _fhirDataStoreFactory = fhirDataStoreFactory;
            _resourceWrapperFactory = resourceWrapperFactory;
            _searchParameterStatusManager = searchParameterStatusManager;
            _searchParameterOperations = searchParameterOperations;
            _logger = logger;
        }

        public async Task<string> ExecuteAsync(JobInfo jobInfo, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(jobInfo, nameof(jobInfo));

            _jobInfo = jobInfo;
            _reindexProcessingJobDefinition = DeserializeJobDefinition(_jobInfo);

            await CheckDiscrepancies(cancellationToken);

            _reindexProcessingJobResult = new ReindexProcessingJobResult();

            // Initialize effective batch size to configured value - may be reduced on OOM
            _effectiveBatchSize = (int)_reindexProcessingJobDefinition.MaximumNumberOfResourcesPerQuery;

            await ProcessQueryAsync(cancellationToken);

            return JsonConvert.SerializeObject(_reindexProcessingJobResult);
        }

        private async Task CheckDiscrepancies(CancellationToken cancellationToken)
        {
            var resourceType = _reindexProcessingJobDefinition.ResourceType;
            var searchParameterHash = _searchParameterOperations.GetSearchParameterHash(resourceType);
            var requestedSearchParameterHash = _reindexProcessingJobDefinition.SearchParameterHash;
            var isBad = requestedSearchParameterHash != searchParameterHash;
            var msg = $"ResourceType={resourceType} SearchParameterHash: Requested={requestedSearchParameterHash} {(isBad ? "!=" : "=")} Current={searchParameterHash}";
            if (isBad)
            {
                _logger.LogJobWarning(_jobInfo, msg);
                await TryLogEvent($"ReindexProcessingJob={_jobInfo.Id}.GetResourcesToReindexAsync", "Error", msg, null, cancellationToken); // elevate in SQL to log w/o extra settings

                // Wait for background cache refresh cycles to bring this host's cache up to date
                _logger.LogJobInformation(_jobInfo, "Waiting for background cache refresh cycles to resolve hash mismatch...");
                await _searchParameterOperations.WaitForRefreshCyclesAsync(3, cancellationToken);

                // Re-read hash after refresh cycles
                searchParameterHash = _searchParameterOperations.GetSearchParameterHash(resourceType);
                isBad = requestedSearchParameterHash != searchParameterHash;
                if (isBad)
                {
                    msg = $"ResourceType={resourceType} SearchParameterHash still mismatched after waiting for cache refresh cycles: Requested={requestedSearchParameterHash} != Current={searchParameterHash}";
                    _logger.LogJobError(_jobInfo, msg);
                    await TryLogEvent($"ReindexProcessingJob={_jobInfo.Id}.GetResourcesToReindexAsync", "Error", msg, null, cancellationToken);
                    throw new ReindexJobException(msg);
                }

                msg = $"ResourceType={resourceType} SearchParameterHash resolved after cache refresh cycles: Requested={requestedSearchParameterHash} = Current={searchParameterHash}";
                _logger.LogJobInformation(_jobInfo, msg);
                await TryLogEvent($"ReindexProcessingJob={_jobInfo.Id}.GetResourcesToReindexAsync", "Warn", msg, null, cancellationToken);
            }
            else
            {
                _logger.LogJobInformation(_jobInfo, msg);
                await TryLogEvent($"ReindexProcessingJob={_jobInfo.Id}.GetResourcesToReindexAsync", "Warn", msg, null, cancellationToken); // elevate in SQL to log w/o extra settings
            }

            // use the same value as used in resource writes
            _searchParameterHash = searchParameterHash;

            var currentDate = _searchParameterOperations.SearchParamLastUpdated.HasValue ? _searchParameterOperations.SearchParamLastUpdated.Value : DateTimeOffset.MinValue;
            var current = currentDate.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var requested = _reindexProcessingJobDefinition.SearchParamLastUpdated.ToString("yyyy-MM-dd HH:mm:ss.fff");
            isBad = _reindexProcessingJobDefinition.SearchParamLastUpdated > currentDate;
            msg = $"SearchParamLastUpdated: Requested={requested} {(isBad ? ">" : "<=")} Current={current}";
            //// If timestamp from definition (requested by orchestrator) is more recent, then cache on processing VM is stale.
            if (isBad)
            {
                _logger.LogJobWarning(_jobInfo, msg);
                await TryLogEvent($"ReindexProcessingJob={_jobInfo.Id}.ExecuteAsync", "Error", msg, null, cancellationToken); // elevate in SQL to log w/o extra settings

                // Cache already refreshed via hash wait above, but timestamp may still lag.
                // Log the discrepancy but proceed — the hash match is the critical check.
            }
            else // normal
            {
                _logger.LogJobInformation(_jobInfo, msg);
                await TryLogEvent($"ReindexProcessingJob={_jobInfo.Id}.ExecuteAsync", "Warn", msg, null, cancellationToken); // elevate in SQL to log w/o extra settings
            }
        }

        private async Task<SearchResult> GetResourcesToReindexAsync(SearchResultReindex searchResultReindex, CancellationToken cancellationToken)
        {
            var queryParametersList = new List<Tuple<string, string>>()
            {
                Tuple.Create(KnownQueryParameterNames.Type, _reindexProcessingJobDefinition.ResourceType),
            };

            int batchSize = _effectiveBatchSize;

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

                    // SQL surrogate-range path uses server-selected ranges. OOM mitigation is handled by
                    // splitting ranges via GetSurrogateIdRanges instead of adding a _count hint.
                }
                else
                {
                    // Cosmos DB path uses _count based on effective batch size.
                    queryParametersList.Add(Tuple.Create(KnownQueryParameterNames.Count, batchSize.ToString()));
                }

                if (searchResultReindex.ContinuationToken != null)
                {
                    queryParametersList.Add(Tuple.Create(KnownQueryParameterNames.ContinuationToken, searchResultReindex.ContinuationToken));
                }
            }
            else
            {
                // Cosmos DB path with no query state provided still needs explicit _count to enforce memory-safe paging.
                queryParametersList.Add(Tuple.Create(KnownQueryParameterNames.Count, batchSize.ToString()));
            }

            using (IScoped<ISearchService> searchService = _searchServiceFactory())
            {
                try
                {
                    return await searchService.Value.SearchForReindexAsync(queryParametersList, _searchParameterHash, false, cancellationToken, true);
                }
                catch (OutOfMemoryException)
                {
                    // Let OutOfMemoryException bubble up so the top-level handler can soft-fail the job.
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogJobError(ex, _jobInfo, "Error running reindex query for resource type {ResourceType}.", _reindexProcessingJobDefinition.ResourceType);
                    throw;
                }
            }
        }

        private void SetJobError(string errorMessage)
        {
            long totalResourceCount = _reindexProcessingJobDefinition?.ResourceCount?.Count ?? 0;
            long failedResourceCount = totalResourceCount - _reindexProcessingJobResult.SucceededResourceCount;

            _reindexProcessingJobResult.Error = errorMessage;
            _reindexProcessingJobResult.FailedResourceCount = failedResourceCount > 0 ? failedResourceCount : 0;
        }

        private bool TryReduceEffectiveBatchSize()
        {
            // Never increase batch size while handling OOM. If already at or below the minimum threshold,
            // keep the current value and signal that no further reduction is possible.
            if (_effectiveBatchSize <= MinEffectiveBatchSize)
            {
                return false;
            }

            int reducedBatchSize = Math.Max(MinEffectiveBatchSize, _effectiveBatchSize / OomReductionFactor);
            reducedBatchSize = Math.Min(reducedBatchSize, _effectiveBatchSize);

            if (reducedBatchSize == _effectiveBatchSize)
            {
                return false;
            }

            _effectiveBatchSize = reducedBatchSize;
            return true;
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

            try
            {
                _reindexProcessingJobResult.SearchParameterUrls = _reindexProcessingJobDefinition.SearchParameterUrls;

                // Determine if we're using SQL Server path (surrogate ID range) or Cosmos DB path (continuation tokens)
                bool useSurrogateIdRange = _reindexProcessingJobDefinition.ResourceCount != null
                    && _reindexProcessingJobDefinition.ResourceCount.StartResourceSurrogateId > 0
                    && _reindexProcessingJobDefinition.ResourceCount.EndResourceSurrogateId > 0;

                if (useSurrogateIdRange)
                {
                    // SQL Server path: Fetch resources in memory-safe batches using surrogate ID ranges
                    // to prevent OutOfMemoryException when processing large batches with large resources
                    await ProcessWithSurrogateIdBatchingAsync(_searchParameterHash, cancellationToken);
                }
                else
                {
                    // Cosmos DB path: Use continuation tokens for pagination
                    await ProcessWithContinuationTokensAsync(_searchParameterHash, cancellationToken);
                }

                if (!cancellationToken.IsCancellationRequested)
                {
                    _logger.LogJobInformation(_jobInfo, "Reindex processing job complete. Total number of resources indexed by this job: {Progress}.", _reindexProcessingJobResult.SucceededResourceCount);
                }
            }
            catch (SqlException sqlEx)
            {
                // For non-timeout SQL errors
                _logger.LogJobError(sqlEx, _jobInfo, "SQL error occurred during reindex processing.");
                SetJobError($"SQL Error: {sqlEx.Message}");

                throw new JobExecutionSoftFailureException($"SQL error occurred during reindex processing: {sqlEx.Message}", _reindexProcessingJobResult, sqlEx, isCustomerCaused: false);
            }
            catch (OutOfMemoryException oomEx)
            {
                string errorMsg = $"OutOfMemoryException occurred during reindex processing for resource type {_reindexProcessingJobDefinition.ResourceType}. Final batch size was {_effectiveBatchSize}.";
                _logger.LogJobError(oomEx, _jobInfo, errorMsg);
                SetJobError(errorMsg);

                throw new JobExecutionSoftFailureException(errorMsg, _reindexProcessingJobResult, oomEx, isCustomerCaused: false);
            }
            catch (FhirException ex)
            {
                _logger.LogJobError(ex, _jobInfo, "Reindex processing job error occurred. Is FhirException: 'true'.");
                SetJobError(ex.Message);

                throw new JobExecutionSoftFailureException(ex.Message, _reindexProcessingJobResult, ex, isCustomerCaused: false);
            }
            catch (Exception ex)
            {
                _logger.LogJobError(ex, _jobInfo, "Reindex processing job error occurred. Is FhirException: 'false'.");
                SetJobError(ex.Message);

                throw new JobExecutionSoftFailureException(ex.Message, _reindexProcessingJobResult, ex, isCustomerCaused: false);
            }
        }

        /// <summary>
        /// Processes resources using surrogate ID ranges for SQL Server.
        /// Uses the configured batch size by default, but switches to smaller batches if OutOfMemoryException occurs.
        /// </summary>
        private async Task ProcessWithSurrogateIdBatchingAsync(string searchParameterHash, CancellationToken cancellationToken)
        {
            long initialStartId = _reindexProcessingJobDefinition.ResourceCount.StartResourceSurrogateId;
            long initialEndId = _reindexProcessingJobDefinition.ResourceCount.EndResourceSurrogateId;
            var rangeQueue = new Queue<(long StartId, long EndId, int Count, int OomReductionCount)>();
            int initialCount = (int)Math.Min(_reindexProcessingJobDefinition.ResourceCount.Count, int.MaxValue);
            rangeQueue.Enqueue((initialStartId, initialEndId, initialCount, 0));

            _logger.LogJobInformation(
                _jobInfo,
                "Starting reindex with surrogate ID range. StartId={StartId}, EndId={EndId}, BatchSize={BatchSize}",
                initialStartId,
                initialEndId,
                _effectiveBatchSize);

            while (rangeQueue.Count > 0 && !cancellationToken.IsCancellationRequested)
            {
                var workItem = rangeQueue.Dequeue();

                var batchSearchResult = new SearchResultReindex(workItem.Count)
                {
                    StartResourceSurrogateId = workItem.StartId,
                    EndResourceSurrogateId = workItem.EndId,
                };

                int batchResourceCount;
                SearchResult result;

                try
                {
                    result = await _timeoutRetries.ExecuteAsync(
                        async () => await GetResourcesToReindexAsync(batchSearchResult, cancellationToken));

                    if (result == null)
                    {
                        throw new OperationFailedException("Search service returned null search result.", HttpStatusCode.InternalServerError);
                    }

                    batchResourceCount = result.Results?.Count() ?? 0;
                    if (batchResourceCount == 0)
                    {
                        _logger.LogJobInformation(_jobInfo, "No resources found in surrogate ID range. StartId={StartId}, EndId={EndId}", workItem.StartId, workItem.EndId);
                        continue;
                    }

                    await _timeoutRetries.ExecuteAsync(
                        async () => await ProcessSearchResultsAsync(result, searchParameterHash, (int)_reindexProcessingJobDefinition.MaximumNumberOfResourcesPerWrite, cancellationToken));
                }
                catch (OutOfMemoryException oomEx)
                {
                    await SplitAndQueueSubRangesAsync(workItem, rangeQueue, "surrogate ID resource fetch or search results processing", oomEx, cancellationToken);
                    continue;
                }

                _reindexProcessingJobResult.SucceededResourceCount += batchResourceCount;
                _jobInfo.Data = _reindexProcessingJobResult.SucceededResourceCount;

                _logger.LogJobInformation(
                    _jobInfo,
                    "Reindex range complete. RangeStart={RangeStart}, RangeEnd={RangeEnd}, BatchSize={BatchSize}, TotalProcessed={TotalProcessed}",
                    workItem.StartId,
                    workItem.EndId,
                    batchResourceCount,
                    _reindexProcessingJobResult.SucceededResourceCount);

                // If the store returned a partial window for this surrogate range, enqueue the remaining tail.
                // This preserves existing range-walk behavior when SQL limits the returned set.
                if (result.MaxResourceSurrogateId > 0 && result.MaxResourceSurrogateId < workItem.EndId)
                {
                    long nextStartId = result.MaxResourceSurrogateId + 1;
                    int remainingCount = Math.Max(0, workItem.Count - batchResourceCount);
                    rangeQueue.Enqueue((nextStartId, workItem.EndId, remainingCount, workItem.OomReductionCount));
                }
            }
        }

        private async Task SplitAndQueueSubRangesAsync(
            (long StartId, long EndId, int Count, int OomReductionCount) failedRange,
            Queue<(long StartId, long EndId, int Count, int OomReductionCount)> rangeQueue,
            string operationLabel,
            OutOfMemoryException oomEx,
            CancellationToken cancellationToken)
        {
            int previousBatchSize = _effectiveBatchSize;
            bool wasReduced = TryReduceEffectiveBatchSize();

            if (!wasReduced)
            {
                _logger.LogJobError(
                    oomEx,
                    _jobInfo,
                    "OutOfMemoryException persisted during {OperationLabel}. Batch size already at minimum {MinBatchSize}. RangeStart={StartId}, RangeEnd={EndId}.",
                    operationLabel,
                    MinEffectiveBatchSize,
                    failedRange.StartId,
                    failedRange.EndId);

                throw oomEx;
            }

            int reductionCount = failedRange.OomReductionCount + 1;
            if (reductionCount > MaxOomReductionsBeforeSoftFail)
            {
                _logger.LogJobError(
                    oomEx,
                    _jobInfo,
                    "OutOfMemoryException persisted after {MaxAttempts} reductions during {OperationLabel}. CurrentBatchSize={CurrentBatchSize}, RangeStart={StartId}, RangeEnd={EndId}.",
                    MaxOomReductionsBeforeSoftFail,
                    operationLabel,
                    _effectiveBatchSize,
                    failedRange.StartId,
                    failedRange.EndId);

                throw oomEx;
            }

            int rangeSize = _effectiveBatchSize;
            int numberOfRanges = Math.Max(1, (int)Math.Ceiling((double)previousBatchSize / _effectiveBatchSize));

            _logger.LogJobWarning(
                oomEx,
                _jobInfo,
                "OutOfMemoryException during {OperationLabel}. Splitting range StartId={StartId}, EndId={EndId}. ReductionAttempt={ReductionAttempt}/{MaxAttempts}, PreviousBatchSize={PreviousBatchSize}, NextBatchSize={NextBatchSize}, RangeSize={RangeSize}, NumberOfRanges={NumberOfRanges}.",
                operationLabel,
                failedRange.StartId,
                failedRange.EndId,
                reductionCount,
                MaxOomReductionsBeforeSoftFail,
                previousBatchSize,
                _effectiveBatchSize,
                rangeSize,
                numberOfRanges);

            IReadOnlyList<(long StartId, long EndId, int Count)> subRanges;
            using (IScoped<ISearchService> searchService = _searchServiceFactory())
            {
                subRanges = await searchService.Value.GetSurrogateIdRanges(
                    _reindexProcessingJobDefinition.ResourceType,
                    failedRange.StartId,
                    failedRange.EndId,
                    rangeSize,
                    numberOfRanges,
                    true,
                    cancellationToken,
                    true);
            }

            if (subRanges == null || subRanges.Count == 0)
            {
                _logger.LogJobError(
                    _jobInfo,
                    "Failed to split surrogate range after OOM. No sub-ranges returned for StartId={StartId}, EndId={EndId}.",
                    failedRange.StartId,
                    failedRange.EndId);
                throw oomEx;
            }

            foreach (var range in subRanges)
            {
                rangeQueue.Enqueue((range.StartId, range.EndId, range.Count, reductionCount));
            }
        }

        /// <summary>
        /// Processes resources using continuation tokens for Cosmos DB.
        /// </summary>
        private async Task ProcessWithContinuationTokensAsync(string searchParameterHash, CancellationToken cancellationToken)
        {
            long totalResourceCount = 0;

            // Keep local query state so we do not mutate the original job definition during continuation paging.
            SearchResultReindex queryState = _reindexProcessingJobDefinition.ResourceCount == null
                ? new SearchResultReindex(_reindexProcessingJobDefinition.MaximumNumberOfResourcesPerQuery)
                : new SearchResultReindex(_reindexProcessingJobDefinition.ResourceCount.Count)
                {
                    StartResourceSurrogateId = _reindexProcessingJobDefinition.ResourceCount.StartResourceSurrogateId,
                    EndResourceSurrogateId = _reindexProcessingJobDefinition.ResourceCount.EndResourceSurrogateId,
                    ContinuationToken = _reindexProcessingJobDefinition.ResourceCount.ContinuationToken,
                };

            _logger.LogJobInformation(
                _jobInfo,
                "Starting reindex with continuation tokens. BatchSize={BatchSize}",
                _effectiveBatchSize);

            SearchResult result;
            try
            {
                result = await _timeoutRetries.ExecuteAsync(
                    async () => await GetResourcesToReindexAsync(queryState, cancellationToken));
            }
            catch (OutOfMemoryException oomEx)
            {
                // Reduce batch size and retry.
                if (!TryReduceEffectiveBatchSize())
                {
                    throw;
                }

                _logger.LogJobWarning(
                    oomEx,
                    _jobInfo,
                    "OutOfMemoryException caught during initial resource fetch. Reducing batch size to {BatchSize} and retrying.",
                    _effectiveBatchSize);

                result = await _timeoutRetries.ExecuteAsync(
                    async () => await GetResourcesToReindexAsync(queryState, cancellationToken));
            }

            if (result == null)
            {
                throw new OperationFailedException("Search service returned null search result.", HttpStatusCode.InternalServerError);
            }

            // Process results in a loop to handle continuation tokens
            do
            {
                int batchResourceCount = result.Results?.Count() ?? 0;
                if (batchResourceCount == 0)
                {
                    _logger.LogJobInformation(_jobInfo, "No more resources found in result set.");
                    break;
                }

                await _timeoutRetries.ExecuteAsync(
                    async () => await ProcessSearchResultsAsync(result, searchParameterHash, (int)_reindexProcessingJobDefinition.MaximumNumberOfResourcesPerWrite, cancellationToken));

                _reindexProcessingJobResult.SucceededResourceCount += batchResourceCount;
                totalResourceCount += batchResourceCount;
                _jobInfo.Data = _reindexProcessingJobResult.SucceededResourceCount;

                _logger.LogJobInformation(
                    _jobInfo,
                    "Reindex batch complete. BatchSize={BatchSize}, TotalProcessed={TotalProcessed}",
                    batchResourceCount,
                    _reindexProcessingJobResult.SucceededResourceCount);

                // Check if there's a continuation token to fetch more results
                if (!string.IsNullOrEmpty(result.ContinuationToken) && !cancellationToken.IsCancellationRequested)
                {
                    _logger.LogJobInformation(_jobInfo, "Continuation token found. Fetching next batch of resources for reindexing.");

                    // Clear the previous continuation token first to avoid conflicts
                    queryState.ContinuationToken = null;

                    // Create a new SearchResultReindex with the continuation token for the next query
                    var nextSearchResultReindex = new SearchResultReindex(queryState.Count)
                    {
                        StartResourceSurrogateId = queryState.StartResourceSurrogateId,
                        EndResourceSurrogateId = queryState.EndResourceSurrogateId,
                        ContinuationToken = Convert.ToBase64String(Encoding.UTF8.GetBytes(result.ContinuationToken)),
                    };

                    // Fetch the next batch of results - handle potential OOM.
                    try
                    {
                        result = await _timeoutRetries.ExecuteAsync(
                            async () => await GetResourcesToReindexAsync(nextSearchResultReindex, cancellationToken));
                    }
                    catch (OutOfMemoryException oomEx)
                    {
                        // Reduce batch size and retry.
                        if (!TryReduceEffectiveBatchSize())
                        {
                            throw;
                        }

                        _logger.LogJobWarning(
                            oomEx,
                            _jobInfo,
                            "OutOfMemoryException caught during continuation fetch. Reducing batch size to {BatchSize} and retrying.",
                            _effectiveBatchSize);

                        result = await _timeoutRetries.ExecuteAsync(
                            async () => await GetResourcesToReindexAsync(nextSearchResultReindex, cancellationToken));
                    }

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

            if (totalResourceCount > _reindexProcessingJobDefinition.MaximumNumberOfResourcesPerQuery)
            {
                _logger.LogJobWarning(
                    _jobInfo,
                    "Reindex: number of resources processed is higher than the original limit. Total count: {TotalCount}. Original limit: {OriginalLimit}",
                    totalResourceCount,
                    _reindexProcessingJobDefinition.MaximumNumberOfResourcesPerQuery);
            }
        }

        /// <summary>
        /// For each result in a batch of resources this will extract new search params
        /// Then compare those to the old values to determine if an update is needed
        /// Needed updates will be committed in a batch
        /// </summary>
        public async Task ProcessSearchResultsAsync(SearchResult results, string searchParameterHash, int batchSize, CancellationToken cancellationToken)
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
                entry.Resource.SearchParameterHash = searchParameterHash;
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
                    try
                    {
                        await _bulkUpdateRetries.ExecuteAsync(
                            async () => await store.Value.BulkUpdateSearchParameterIndicesAsync(batch, cancellationToken));
                    }
                    catch (PreconditionFailedException ex)
                    {
                        // Version conflicts can occur when resources are updated during reindex.
                        // Log warning and continue - conflicting resources will be picked up in the next reindex cycle.
                        _logger.LogWarning(ex, "Version conflict during reindex batch update. Some resources were modified during reindex and will be reprocessed in a subsequent cycle.");
                    }

                    if (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }
                }
            }
        }

        private async Task TryLogEvent(string process, string status, string text, DateTime? startDate, CancellationToken cancellationToken)
        {
            using IScoped<IFhirDataStore> store = _fhirDataStoreFactory();
            await store.Value.TryLogEvent(process, status, text, startDate, cancellationToken);
        }

        private static ReindexProcessingJobDefinition DeserializeJobDefinition(JobInfo jobInfo)
        {
            return JsonConvert.DeserializeObject<ReindexProcessingJobDefinition>(jobInfo.Definition);
        }
    }
}
