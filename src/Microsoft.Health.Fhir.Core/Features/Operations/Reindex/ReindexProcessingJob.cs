// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
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
        private readonly ISearchParameterOperations _searchParameterOperations;

        private JobInfo _jobInfo;
        private ReindexProcessingJobResult _result;
        private ReindexProcessingJobDefinition _definition;
        private bool _isSql;
        private string _searchParameterHash;
        private const int MaxTimeoutRetries = 3;

        private CancellationToken _cancellationToken;

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
            _cancellationToken = cancellationToken;
            _jobInfo = jobInfo;
            _definition = JsonConvert.DeserializeObject<ReindexProcessingJobDefinition>(jobInfo.Definition);
            //// Determine if we're using SQL Server (surrogate ID range) or Cosmos DB (continuation tokens)
            _isSql = _definition.ResourceCount != null && _definition.ResourceCount.StartResourceSurrogateId > 0 && _definition.ResourceCount.EndResourceSurrogateId > 0;

            await CheckDiscrepancies();

            _result = new ReindexProcessingJobResult();

            await ProcessQueryAsync();

            return JsonConvert.SerializeObject(_result);
        }

        private async Task CheckDiscrepancies()
        {
            var resourceType = _definition.ResourceType;
            var searchParameterHash = _searchParameterOperations.GetSearchParameterHash(resourceType);
            var requestedSearchParameterHash = _definition.SearchParameterHash;
            var isBad = requestedSearchParameterHash != searchParameterHash;
            var msg = $"ResourceType={resourceType} SearchParameterHash: Requested={requestedSearchParameterHash} {(isBad ? "!=" : "=")} Current={searchParameterHash}";
            if (isBad)
            {
                _logger.LogJobError(_jobInfo, msg);
                await TryLogEvent($"ReindexProcessingJob={_jobInfo.Id}.GetResourcesToReindexAsync", "Error", msg, null); // elevate in SQL to log w/o extra settings
                throw new ReindexJobException(msg);
            }
            else
            {
                _logger.LogJobInformation(_jobInfo, msg);
                await TryLogEvent($"ReindexProcessingJob={_jobInfo.Id}.GetResourcesToReindexAsync", "Warn", msg, null); // elevate in SQL to log w/o extra settings
            }

            // use the same value as used in resource writes
            _searchParameterHash = searchParameterHash;

            var currentDate = _searchParameterOperations.SearchParamLastUpdated;
            var current = currentDate.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var requested = _definition.SearchParamLastUpdated.ToString("yyyy-MM-dd HH:mm:ss.fff");
            isBad = _definition.SearchParamLastUpdated > currentDate;
            msg = $"SearchParamLastUpdated: Requested={requested} {(isBad ? ">" : "<=")} Current={current}";
            //// If timestamp from definition (requested by orchestrator) is more recent, then cache on processing VM is stale.
            if (isBad)
            {
                _logger.LogJobError(_jobInfo, msg);
                await TryLogEvent($"ReindexProcessingJob={_jobInfo.Id}.ExecuteAsync", "Error", msg, null); // elevate in SQL to log w/o extra settings
                throw new ReindexJobException(msg);
            }
            else // normal
            {
                _logger.LogJobInformation(_jobInfo, msg);
                await TryLogEvent($"ReindexProcessingJob={_jobInfo.Id}.ExecuteAsync", "Warn", msg, null); // elevate in SQL to log w/o extra settings
            }
        }

        private async Task<SearchResult> GetResourcesToReindexAsync(SearchResultReindex query)
        {
            var queryParametersList = new List<Tuple<string, string>>()
            {
                Tuple.Create(KnownQueryParameterNames.Type, _definition.ResourceType),
            };

            // If we have SurrogateId range, it is SQL. We simply use those and ignore search parameter hash
            if (query.StartResourceSurrogateId > 0 && query.EndResourceSurrogateId > 0)
            {
                queryParametersList.Add(Tuple.Create(KnownQueryParameterNames.IgnoreSearchParamHash, "true"));

                queryParametersList.AddRange(
                [
                    Tuple.Create(KnownQueryParameterNames.StartSurrogateId, query.StartResourceSurrogateId.ToString()),
                    Tuple.Create(KnownQueryParameterNames.EndSurrogateId, query.EndResourceSurrogateId.ToString()),
                ]);
            }
            else
            {
                // Otherwise, it's cosmos DB and we must use it and ensure we pass MaximumNumberOfResourcesPerQuery so we get expected count returned.
                queryParametersList.Add(Tuple.Create(KnownQueryParameterNames.Count, _definition.MaximumNumberOfResourcesPerQuery.ToString()));
            }

            if (query.ContinuationToken != null)
            {
                queryParametersList.Add(Tuple.Create(KnownQueryParameterNames.ContinuationToken, query.ContinuationToken));
            }

            using var searchService = _searchServiceFactory();
            try
            {
                return await searchService.Value.SearchForReindexAsync(queryParametersList, _searchParameterHash, false, _cancellationToken, true);
            }
            catch (Exception ex)
            {
                _logger.LogJobError(ex, _jobInfo, "Error running reindex query for resource type {ResourceType}.", _definition.ResourceType);
                throw;
            }
        }

        private void SetJobError(string errorMessage)
        {
            var totalResourceCount = _definition?.ResourceCount?.Count ?? 0;
            var failedResourceCount = totalResourceCount - _result.SucceededResourceCount;
            _result.Error = errorMessage;
            _result.FailedResourceCount = failedResourceCount > 0 ? failedResourceCount : 0;
        }

        private async Task ProcessQueryAsync()
        {
            if (_cancellationToken.IsCancellationRequested)
            {
                return;
            }

            try
            {
                if (_isSql)
                {
                    await ProcessWithSurrogateIdRangeAsync();
                }
                else
                {
                    await ProcessWithContinuationTokensAsync(_searchParameterHash);
                }

                if (!_cancellationToken.IsCancellationRequested)
                {
                    _logger.LogJobInformation(_jobInfo, "Reindex processing job complete. Total number of resources indexed by this job: {Progress}.", _result.SucceededResourceCount);
                }
            }
            catch (SqlException sqlEx)
            {
                // For non-timeout SQL errors
                _logger.LogJobError(sqlEx, _jobInfo, "SQL error occurred during reindex processing.");
                SetJobError($"SQL Error: {sqlEx.Message}");

                throw new JobExecutionSoftFailureException($"SQL error occurred during reindex processing: {sqlEx.Message}", _result, sqlEx, isCustomerCaused: false);
            }
            catch (FhirException ex)
            {
                _logger.LogJobError(ex, _jobInfo, "Reindex processing job error occurred. Is FhirException: 'true'.");
                SetJobError(ex.Message);

                throw new JobExecutionSoftFailureException(ex.Message, _result, ex, isCustomerCaused: false);
            }
            catch (Exception ex)
            {
                _logger.LogJobError(ex, _jobInfo, "Reindex processing job error occurred. Is FhirException: 'false'.");
                SetJobError(ex.Message);

                throw new JobExecutionSoftFailureException(ex.Message, _result, ex, isCustomerCaused: false);
            }
        }

        /// <summary>
        /// Processes resources using surrogate ID ranges for SQL Server.
        /// </summary>
        private async Task ProcessWithSurrogateIdRangeAsync()
        {
            var startId = _definition.ResourceCount.StartResourceSurrogateId;
            var endId = _definition.ResourceCount.EndResourceSurrogateId;
            _logger.LogJobInformation(_jobInfo, "SQL reindex range start. StartId={StartId}, EndId={EndId}, BatchSize={BatchSize}", startId, endId, _definition.MaximumNumberOfResourcesPerQuery);

            var query = new SearchResultReindex() { StartResourceSurrogateId = startId, EndResourceSurrogateId = endId };
            var result = await _timeoutRetries.ExecuteAsync(async () => await GetResourcesToReindexAsync(query));
            var resourceCount = result.Results?.Count() ?? 0;

            await _timeoutRetries.ExecuteAsync(async () => await ProcessSearchResultsAsync(result, null, (int)_definition.MaximumNumberOfResourcesPerWrite, _cancellationToken));

            _result.SucceededResourceCount += resourceCount;
            _jobInfo.Data = _result.SucceededResourceCount;
            _logger.LogJobInformation(_jobInfo, "SQL reindex range complete. Start={RangeStart}, End={RangeEnd}, Size={BatchSize}, Processed={TotalProcessed}", startId, endId, resourceCount, _result.SucceededResourceCount);
        }

        /// <summary>
        /// Processes resources using continuation tokens for Cosmos DB.
        /// </summary>
        private async Task ProcessWithContinuationTokensAsync(string searchParameterHash)
        {
            var totalResourceCount = 0L;

            // Keep local query state so we do not mutate the original job definition during continuation paging.
            var query = _definition.ResourceCount == null
                      ? new SearchResultReindex(_definition.MaximumNumberOfResourcesPerQuery)
                      : new SearchResultReindex(_definition.ResourceCount.Count)
                        {
                            StartResourceSurrogateId = _definition.ResourceCount.StartResourceSurrogateId,
                            EndResourceSurrogateId = _definition.ResourceCount.EndResourceSurrogateId,
                            ContinuationToken = _definition.ResourceCount.ContinuationToken,
                        };

            _logger.LogJobInformation(_jobInfo, "Cosmos reindex starts. BatchSize={BatchSize}", _definition.MaximumNumberOfResourcesPerQuery);

            var result = await _timeoutRetries.ExecuteAsync(async () => await GetResourcesToReindexAsync(query));

            // Process results in a loop to handle continuation tokens
            do
            {
                var batchResourceCount = result.Results?.Count() ?? 0;

                await _timeoutRetries.ExecuteAsync(async () => await ProcessSearchResultsAsync(result, searchParameterHash, (int)_definition.MaximumNumberOfResourcesPerWrite, _cancellationToken));

                _result.SucceededResourceCount += batchResourceCount;
                totalResourceCount += batchResourceCount;
                _jobInfo.Data = _result.SucceededResourceCount;

                _logger.LogJobInformation(_jobInfo, "Cosmos reindex batch complete. BatchSize={BatchSize}, TotalProcessed={TotalProcessed}", batchResourceCount, _result.SucceededResourceCount);

                // Check if there's a continuation token to fetch more results
                if (!string.IsNullOrEmpty(result.ContinuationToken) && !_cancellationToken.IsCancellationRequested)
                {
                    _logger.LogJobInformation(_jobInfo, "Cosmos continuation token found. Fetching next batch of resources for reindexing.");

                    // Create a new SearchResultReindex with the continuation token for the next query
                    var nextQuery = new SearchResultReindex(query.Count)
                    {
                        StartResourceSurrogateId = query.StartResourceSurrogateId,
                        EndResourceSurrogateId = query.EndResourceSurrogateId,
                        ContinuationToken = Convert.ToBase64String(Encoding.UTF8.GetBytes(result.ContinuationToken)),
                    };

                    result = await _timeoutRetries.ExecuteAsync(async () => await GetResourcesToReindexAsync(nextQuery));
                }
                else
                {
                    // No more continuation token, exit the loop
                    result = null;
                }
            }
            while (result != null && !_cancellationToken.IsCancellationRequested);

            if (totalResourceCount > _definition.MaximumNumberOfResourcesPerQuery)
            {
                _logger.LogJobWarning(_jobInfo, "Cosmos reindex: number of resources processed is higher than the original limit. Total count: {TotalCount}. Original limit: {OriginalLimit}", totalResourceCount, _definition.MaximumNumberOfResourcesPerQuery);
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
            foreach (var entry in results.Results)
            {
                entry.Resource.SearchParameterHash = searchParameterHash;
                _resourceWrapperFactory.Update(entry.Resource);
                updateSearchIndices.Add(entry.Resource);
            }

            using var store = _fhirDataStoreFactory();
            for (var i = 0; i < updateSearchIndices.Count; i += batchSize)
            {
                var batch = updateSearchIndices.GetRange(i, Math.Min(batchSize, updateSearchIndices.Count - i));
                await _bulkUpdateRetries.ExecuteAsync(async () => await store.Value.BulkUpdateSearchParameterIndicesAsync(batch, cancellationToken));

                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
            }
        }

        private async Task TryLogEvent(string process, string status, string text, DateTime? startDate)
        {
            using IScoped<IFhirDataStore> store = _fhirDataStoreFactory();
            await store.Value.TryLogEvent(process, status, text, startDate, _cancellationToken);
        }
    }
}
