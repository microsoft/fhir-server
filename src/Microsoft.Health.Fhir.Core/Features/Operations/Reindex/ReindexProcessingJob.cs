// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
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
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Models;
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

        private static readonly AsyncPolicy _bulkUpdateRetries = Policy.WrapAsync(_requestRateRetries, _timeoutRetries);

        private readonly Func<IScoped<ISearchService>> _searchServiceFactory;
        private readonly IResourceWrapperFactory _resourceWrapperFactory;
        private readonly Func<IScoped<IFhirDataStore>> _fhirDataStoreFactory;
        private readonly ILogger<ReindexProcessingJob> _logger;
        private readonly ISearchParameterOperations _searchParameterOperations;
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager;

        private JobInfo _jobInfo;
        private ReindexProcessingJobResult _result;
        private ReindexProcessingJobDefinition _definition;
        private int _batchSize;
        private bool _isSql;
        private string _searchParameterHash;
        private const int MaxTimeoutRetries = 3;
        private CancellationToken _cancellationToken;

        public ReindexProcessingJob(
            Func<IScoped<ISearchService>> searchServiceFactory,
            Func<IScoped<IFhirDataStore>> fhirDataStoreFactory,
            IResourceWrapperFactory resourceWrapperFactory,
            ISearchParameterOperations searchParameterOperations,
            ISearchParameterDefinitionManager searchParameterDefinitionManager,
            ILogger<ReindexProcessingJob> logger)
        {
            EnsureArg.IsNotNull(searchServiceFactory, nameof(searchServiceFactory));
            EnsureArg.IsNotNull(fhirDataStoreFactory, nameof(fhirDataStoreFactory));
            EnsureArg.IsNotNull(resourceWrapperFactory, nameof(resourceWrapperFactory));
            EnsureArg.IsNotNull(searchParameterOperations, nameof(searchParameterOperations));
            EnsureArg.IsNotNull(searchParameterDefinitionManager, nameof(searchParameterDefinitionManager));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _searchServiceFactory = searchServiceFactory;
            _fhirDataStoreFactory = fhirDataStoreFactory;
            _resourceWrapperFactory = resourceWrapperFactory;
            _searchParameterOperations = searchParameterOperations;
            _searchParameterDefinitionManager = searchParameterDefinitionManager;
            _logger = logger;
        }

        public static int OomRetryDelayBaseSec { get; set; } = 120;

        private AsyncPolicy OomRetries => Policy
            .Handle<OutOfMemoryException>()
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: _ => TimeSpan.FromSeconds(RandomNumberGenerator.GetInt32(OomRetryDelayBaseSec, OomRetryDelayBaseSec * 3)),
                onRetry: (exception, delay, retryCount, context) =>
                {
                    _batchSize = Math.Max(1, _batchSize / 10);
                    _logger.LogJobWarning(_jobInfo, $"Reindex OutOfMemoryException. Reduced batch size={_batchSize}");
                });

        public async Task<string> ExecuteAsync(JobInfo jobInfo, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(jobInfo, nameof(jobInfo));
            _cancellationToken = cancellationToken;
            _jobInfo = jobInfo;
            _definition = JsonConvert.DeserializeObject<ReindexProcessingJobDefinition>(jobInfo.Definition);

            // code will change batch size on OOM retries. Do not reference MaximumNumberOfResourcesPerWrite down below.
            _batchSize = _definition.MaximumNumberOfResourcesPerWrite;

            // Determine if we're using SQL Server (surrogate ID range) or Cosmos DB (continuation tokens)
            _isSql = _definition.ResourceCount != null && _definition.ResourceCount.StartResourceSurrogateId > 0 && _definition.ResourceCount.EndResourceSurrogateId > 0;

            _result = new ReindexProcessingJobResult(); // cosmos logic is incremental, so result has to be initiated outside oom retries
            await CheckSearchParamHash();

            await ProcessAsync();

            return JsonConvert.SerializeObject(_result);
        }

        private async Task CheckSearchParamCache(string resourceType)
        {
            var invalid = await ReindexOrchestratorJob.GetCustomSearchParamsWithoutResources(resourceType, _searchParameterOperations, _searchParameterDefinitionManager, _cancellationToken);
            if (invalid.Any())
            {
                var msg = $"Cache contains search params without resources for resource type={resourceType}: {string.Join(", ", invalid.Select(p => p.Url.OriginalString))}";
                _result.Error = msg;
                throw new JobExecutionSoftFailureException(msg, _result, false);
            }
        }

        private async Task CheckSearchParamHash()
        {
            var resourceType = _definition.ResourceType;
            LogCacheDiag(resourceType);
            var searchParameterHash = _searchParameterOperations.GetSearchParameterHash(resourceType);
            var requestedSearchParameterHash = _definition.SearchParameterHash;
            var isBad = requestedSearchParameterHash != searchParameterHash;
            var msg = $"ResourceType={resourceType} SearchParameterHash: Requested={requestedSearchParameterHash} {(isBad ? "!=" : "=")} Local={searchParameterHash}";
            if (isBad)
            {
                _logger.LogJobError(_jobInfo, msg);
                await TryLogEvent($"ReindexProcessingJob={_jobInfo.Id}.CheckSearchParamHash", "Error", msg, null);

                await CheckSearchParamCache(resourceType);

                _result.Error = msg;
                throw new JobExecutionSoftFailureException(_result.Error, _result, false);
            }
            else
            {
                _logger.LogJobInformation(_jobInfo, msg);
                await TryLogEvent($"ReindexProcessingJob={_jobInfo.Id}.CheckSearchParamHash", "Warn", msg, null);
            }

            _searchParameterHash = searchParameterHash; // this is relevant for cosmos only
        }

        public void LogCacheDiag(string resourceType)
        {
            var searchParameters = _searchParameterDefinitionManager.GetSearchParameters(resourceType).Where(_ => _.SearchParameterStatus == SearchParameterStatus.Supported || _.SearchParameterStatus == SearchParameterStatus.Enabled).ToList();
            var systemCount = searchParameters.Count(_ => _.IsSystemDefined);
            var urls = searchParameters.Where(_ => !_.IsSystemDefined).Select(_ => _.Url.ToString()).OrderBy(_ => _).ToList();
            _logger.LogJobInformation(_jobInfo, $"SearchParam Cache: System={systemCount}, Custom={urls.Count}, CustomUrls=[{string.Join(",", urls)}]");
        }

        private async Task<SearchResult> GetResourcesToReindexAsync(long count, string continuationToken)
        {
            var queryParametersList = new List<Tuple<string, string>>()
            {
                Tuple.Create(KnownQueryParameterNames.Type, _definition.ResourceType),
                Tuple.Create(KnownQueryParameterNames.Count, count.ToString()),
            };

            if (continuationToken != null)
            {
                queryParametersList.Add(Tuple.Create(KnownQueryParameterNames.ContinuationToken, continuationToken));
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

        private async Task ProcessAsync()
        {
            _cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await OomRetries.ExecuteAsync(async () =>
                {
                    if (_isSql)
                    {
                        _result = new ReindexProcessingJobResult(); // sql reruns all, so result has to be initialized on every oom retry
                        await ProcessWithSurrogateIdRangeAsync();
                    }
                    else
                    {
                        await ProcessWithContinuationTokensAsync();
                    }
                });
            }
            catch (SqlException ex)
            {
                _logger.LogJobError(ex, _jobInfo, $"Reindex processing job error occurred. SqlException: {ex.Message}.");
                _result.Error = ex.Message;
                throw new JobExecutionSoftFailureException(_result.Error, _result, ex, false);
            }
            catch (FhirException ex)
            {
                _logger.LogJobError(ex, _jobInfo, $"Reindex processing job error occurred. FhirException: {ex.Message}.");
                _result.Error = ex.Message;
                throw new JobExecutionSoftFailureException(_result.Error, _result, ex, false);
            }
            catch (OutOfMemoryException ex)
            {
                _logger.LogJobError(ex, _jobInfo, $"Reindex processing job error occurred. OutOfMemoryException (exhausted retries): {ex.Message}.");
                _result.Error = ex.Message;
                throw new JobExecutionSoftFailureException(_result.Error, _result, ex, false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogJobError(ex, _jobInfo, $"Reindex processing job error occurred. Exception: {ex.Message}.");
                _result.Error = ex.Message;
                throw new JobExecutionSoftFailureException(_result.Error, _result, ex, false);
            }
        }

        /// <summary>
        /// Processes resources using surrogate ID subRanges for SQL Server.
        /// </summary>
        private async Task ProcessWithSurrogateIdRangeAsync()
        {
            var startId = _definition.ResourceCount.StartResourceSurrogateId;
            var endId = _definition.ResourceCount.EndResourceSurrogateId;
            _logger.LogJobInformation(_jobInfo, $"SQL reindex: Range start. Start={startId}, End={endId}, BatchSize={_batchSize}");

            var subRanges = await _timeoutRetries.ExecuteAsync(async () =>
            {
                var numberOfSubRanges = (int)Math.Ceiling((double)_definition.MaximumNumberOfResourcesPerQuery / _batchSize);
                using var searchService = _searchServiceFactory();
                return await searchService.Value.GetSurrogateIdRanges(_definition.ResourceType, startId, endId, _batchSize, numberOfSubRanges, true, _cancellationToken, true);
            });
            _logger.LogJobInformation(_jobInfo, $"SQL reindex: numberOfSubRanges={subRanges.Count}");

            using var store = _fhirDataStoreFactory();
            foreach (var range in subRanges)
            {
                _cancellationToken.ThrowIfCancellationRequested();

                var resources = await _timeoutRetries.ExecuteAsync(async () =>
                {
                    using var searchService = _searchServiceFactory();
                    return (await searchService.Value.SearchBySurrogateIdRange(_definition.ResourceType, range.StartId, range.EndId, _cancellationToken)).Results.Select(_ => _.Resource).ToList();
                });

                await ComputeAndWrite(resources, store.Value, _cancellationToken);
                _result.SucceededResourceCount += resources.Count;
                _jobInfo.Data = _result.SucceededResourceCount;

                _logger.LogJobInformation(_jobInfo, $"SQL reindex: Subrange complete. Start={range.StartId}, End={range.EndId}, Processed={resources.Count}, TotalProcessed={_result.SucceededResourceCount}");
            }
        }

        /// <summary>
        /// Processes resources using continuation tokens for Cosmos DB.
        /// Resources are fetched one write batch at a time and written immediately,
        /// continuing until there are no more results.
        /// </summary>
        private async Task ProcessWithContinuationTokensAsync()
        {
            _logger.LogJobInformation(_jobInfo, "Cosmos reindex starts. BatchSize={BatchSize}", _batchSize);

            using var store = _fhirDataStoreFactory();
            string continuationToken = null;

            while (true)
            {
                _cancellationToken.ThrowIfCancellationRequested();

                var result = await _timeoutRetries.ExecuteAsync(async () => await GetResourcesToReindexAsync(_batchSize, continuationToken));

                var resources = result.Results?.Select(_ => _.Resource).ToList();
                if (resources?.Count > 0)
                {
                    await ComputeAndWrite(resources, store.Value, _cancellationToken);

                    _result.SucceededResourceCount += resources.Count;
                    _jobInfo.Data = _result.SucceededResourceCount;

                    _logger.LogJobInformation(_jobInfo, "Cosmos reindex batch complete. BatchSize={BatchSize}, TotalProcessed={TotalProcessed}", resources.Count, _result.SucceededResourceCount);
                }

                if (string.IsNullOrEmpty(result.ContinuationToken))
                {
                    break;
                }

                continuationToken = Convert.ToBase64String(Encoding.UTF8.GetBytes(result.ContinuationToken));
            }
        }

        internal async Task ComputeAndWrite(IReadOnlyList<ResourceWrapper> resources, IFhirDataStore store, CancellationToken cancellationToken)
        {
            foreach (var resource in resources)
            {
                _resourceWrapperFactory.Update(resource);
            }

            await _bulkUpdateRetries.ExecuteAsync(async () => await store.BulkUpdateSearchParameterIndicesAsync(resources, cancellationToken));
        }

        private async Task TryLogEvent(string process, string status, string text, DateTime? startDate)
        {
            using IScoped<IFhirDataStore> store = _fhirDataStoreFactory();
            await store.Value.TryLogEvent(process, status, text, startDate, _cancellationToken);
        }
    }
}
