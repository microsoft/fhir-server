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

            _jobInfo = jobInfo;
            _reindexProcessingJobDefinition = DeserializeJobDefinition(jobInfo);
            _reindexProcessingJobResult = new ReindexProcessingJobResult();

            ValidateSearchParametersHash(_jobInfo, _reindexProcessingJobDefinition, cancellationToken);

            await ProcessQueryAsync(cancellationToken);

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
            _logger.LogJobInformation(_jobInfo, $"ReindexProcessingJob Error: Current ReindexJobRecord: {ser}. ReindexProcessing Job Result: {result}.");
        }

        private async Task ProcessQueryAsync(CancellationToken cancellationToken)
        {
            if (_reindexProcessingJobDefinition == null)
            {
                throw new InvalidOperationException("_reindexProcessingJobDefinition cannot be null during processing.");
            }

            long resourceCount = 0;
            try
            {
                SearchResult result = await _timeoutRetries.ExecuteAsync(async () => await GetResourcesToReindexAsync(_reindexProcessingJobDefinition.ResourceCount, cancellationToken));
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

                await _timeoutRetries.ExecuteAsync(async () => await ProcessSearchResultsAsync(result, dictionary, (int)_reindexProcessingJobDefinition.MaximumNumberOfResourcesPerWrite, cancellationToken));

                if (!cancellationToken.IsCancellationRequested)
                {
                    _reindexProcessingJobResult.SucceededResourceCount += (long)result?.Results.Count();
                    _logger.LogJobInformation(_jobInfo, "Reindex processing job complete. Current number of resources indexed by this job: {Progress}.", _reindexProcessingJobResult.SucceededResourceCount);
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
                _reindexProcessingJobResult.FailedResourceCount = resourceCount;
            }
            catch (Exception ex)
            {
                _logger.LogJobError(ex, _jobInfo, "Reindex processing job error occurred. Is FhirException: 'false'.");
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
