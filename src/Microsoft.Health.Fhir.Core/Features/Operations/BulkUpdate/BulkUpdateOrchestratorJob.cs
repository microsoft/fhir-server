// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.Rest;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.JobManagement;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Operations.BulkUpdate
{
    /// <summary>
    /// Orchestrates bulk update operation for FHIR resources by coordinating the creation and enqueuing of processing jobs.
    /// Determines the optimal strategy for partitioning bulk update work:
    /// - If the parallel flag is true and there are no search parameters, partitions jobs by resource type and surrogate ID ranges. (At system level or at resource type level without any search parameters).
    /// - If the parallel flag is true and search parameters are present, runs search queries and creates jobs for each page (continuation token).
    /// - If the parallel flag is false, enqueues a single processing job.
    /// Leverages parallelization for efficient processing.
    /// Each processing job is responsible for reading, matching and referenced(included) resources and updating them.
    /// Ensures context propagation, logging, and robust handling of job execution scenarios.
    /// </summary>
    [JobTypeId((int)JobType.BulkUpdateOrchestrator)]
    public class BulkUpdateOrchestratorJob : IJob
    {
        private readonly IQueueClient _queueClient;
        private readonly RequestContextAccessor<IFhirRequestContext> _contextAccessor;
        private readonly Func<IScoped<ISearchService>> _searchService;
        private readonly ILogger<BulkUpdateOrchestratorJob> _logger;
        private const int CoordinatorMaxDegreeOfParallelization = 4;
        private const int NumberOfParallelRecordRanges = 100;
        private const string OperationCompleted = "Completed";

        public BulkUpdateOrchestratorJob(
            IQueueClient queueClient,
            RequestContextAccessor<IFhirRequestContext> contextAccessor,
            Func<IScoped<ISearchService>> searchService,
            ILogger<BulkUpdateOrchestratorJob> logger)
        {
            EnsureArg.IsNotNull(queueClient, nameof(queueClient));
            EnsureArg.IsNotNull(contextAccessor, nameof(contextAccessor));
            EnsureArg.IsNotNull(searchService, nameof(searchService));

            _queueClient = queueClient;
            _contextAccessor = contextAccessor;
            _searchService = searchService;
            _logger = logger;
        }

        public async Task<string> ExecuteAsync(JobInfo jobInfo, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(jobInfo, nameof(jobInfo));
            IFhirRequestContext existingFhirRequestContext = _contextAccessor.RequestContext;

            BulkUpdateDefinition definition = jobInfo.DeserializeDefinition<BulkUpdateDefinition>();

            try
            {
                Activity.Current?.SetParentId(definition.ParentRequestId);

                var fhirRequestContext = CreateFhirRequestContext(definition, jobInfo);

                _contextAccessor.RequestContext = fhirRequestContext;
                var surrogateIdRangeSize = (int)definition.MaximumNumberOfResourcesPerQuery;

                _logger.LogJobInformation(jobInfo, "Loading job by Group Id.");
                var groupJobs = await _queueClient.GetJobByGroupIdAsync(QueueType.BulkUpdate, jobInfo.GroupId, true, cancellationToken);

                // Check if definition.SearchParameters is not null
                bool noOtherSearchParameters = definition.SearchParameters == null || definition.SearchParameters.Count == 0;
                if (!noOtherSearchParameters)
                {
                    // Collect allowed parameter names (case-insensitive)
                    var allowedParams = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        KnownQueryParameterNames.LastUpdated,
                        KnownQueryParameterNames.MaxCount,
                    };

                    // Check if all parameters are allowed
                    noOtherSearchParameters = definition.SearchParameters.All(p => allowedParams.Contains(p.Item1));
                }

                // For Parallel bulk update, when there are no SearchParameters then create sub jobs by resourceType-surrogateId ranges
                using var searchService = _searchService.Invoke();
                if (definition.IsParallel && noOtherSearchParameters)
                {
                    var resourceTypes = string.IsNullOrEmpty(definition.Type)
                          ? (await searchService.Value.GetUsedResourceTypes(cancellationToken))
                          : definition.Type.Split(',');
                    resourceTypes = resourceTypes.Where(x => !OperationsConstants.ExcludedResourceTypesForBulkUpdate.Contains(x)).ToList();
                    var globalStartId = new PartialDateTime(DateTime.MinValue).ToDateTimeOffset().ToId();
                    var globalEndId = new PartialDateTime(jobInfo.CreateDate).ToDateTimeOffset().ToId() - 1;
                    _logger.LogJobInformation(jobInfo, "Creating bulk update processing jobs by resourceType-surrogateId ranges with Global start surrogate ID: {GlobalStartId}, Global end surrogate ID: {GlobalEndId}", globalStartId, globalEndId);

                    var enqueued = groupJobs.Where(x => x.Id != jobInfo.Id) // exclude coord
                                            .Select(x => JsonConvert.DeserializeObject<BulkUpdateDefinition>(x.Definition))
                                            .Where(x => x.EndSurrogateId != null) // This is to handle current mock tests. It is not needed but does not hurt.
                                            .GroupBy(x => x.Type)
                                            .ToDictionary(x => x.Key, x => x.Max(r => long.Parse(r.EndSurrogateId)));

                    await Parallel.ForEachAsync(resourceTypes, new ParallelOptions { MaxDegreeOfParallelism = CoordinatorMaxDegreeOfParallelization, CancellationToken = cancellationToken }, async (type, cancel) =>
                    {
                        var startId = globalStartId;
                        if (enqueued.TryGetValue(type, out var max))
                        {
                            startId = max + 1;
                        }

                        var rows = 1;
                        while (rows > 0)
                        {
                            var definitions = new List<BulkUpdateDefinition>();
                            var ranges = await searchService.Value.GetSurrogateIdRanges(type, startId, globalEndId, surrogateIdRangeSize, NumberOfParallelRecordRanges, true, cancel);
                            foreach (var range in ranges)
                            {
                                if (range.EndId > startId)
                                {
                                    startId = range.EndId;
                                }

                                _logger.LogJobInformation(jobInfo, "Creating bulk update definition (1).");
                                var processingDefinition = CreateProcessingDefinition(definition, searchService.Value, cancellationToken, type, continuationToken: null, startSurrogateId: range.StartId.ToString(), endSurrogateId: range.EndId.ToString(), globalStartSurrogateId: globalStartId.ToString(), globalEndSurrogateId: globalEndId.ToString());
                                definitions.Add(processingDefinition);
                            }

                            startId++; // make sure we do not intersect ranges

                            rows = definitions.Count;
                            if (rows > 0)
                            {
                                _logger.LogJobInformation(jobInfo, "Enqueuing bulk update job (1).");
                                await _queueClient.EnqueueAsync(QueueType.BulkUpdate, cancel, groupId: jobInfo.GroupId, definitions: definitions.ToArray());
                            }
                        }
                    });
                }
                else if (definition.IsParallel)
                {
                    // For Parallel bulk update, when there are SearchParameters then create sub jobs at continuation token level for matched and included resources.
                    _logger.LogJobInformation(jobInfo, "Creating bulk update processing jobs at page level.");

                    // Let's check for existing jobs to avoid duplicate processing
                    var topJob = groupJobs
                        .Where(x => x.Id != jobInfo.Id)
                        .OrderByDescending(j => j.CreateDate)
                        .FirstOrDefault();

                    string lastEnqueuedMaxContinuationToken = topJob == null
                        ? null
                        : JsonConvert.DeserializeObject<BulkUpdateDefinition>(topJob.Definition).SearchParameters?.FirstOrDefault(sp => sp.Item1.Equals(KnownQueryParameterNames.ContinuationToken, StringComparison.OrdinalIgnoreCase))?.Item2;

                    string nextContinuationToken = null;
                    string prevContinuationToken = null;
                    var definitions = new List<BulkUpdateDefinition>();
                    SearchResult searchResult;

                    var searchParams = definition.SearchParameters?.ToList() ?? new List<Tuple<string, string>>();
                    searchParams.Add(Tuple.Create(KnownQueryParameterNames.Count, definition.MaximumNumberOfResourcesPerQuery.ToString(CultureInfo.InvariantCulture)));
                    if (!string.IsNullOrEmpty(lastEnqueuedMaxContinuationToken))
                    {
                        searchParams.RemoveAll(x => x.Item1.Equals(KnownQueryParameterNames.ContinuationToken, StringComparison.OrdinalIgnoreCase));
                        searchParams.Add(Tuple.Create(KnownQueryParameterNames.ContinuationToken, lastEnqueuedMaxContinuationToken));
                        searchResult = await Search(definition, searchService, searchParams, cancellationToken);

                        if (searchResult.Results != null && searchResult.Results.Any() && !string.IsNullOrEmpty(searchResult.ContinuationToken))
                        {
                            // More results to process
                            // Let's update the ContinuationToken in searchParam so that we can start from the next page
                            searchParams.RemoveAll(x => x.Item1.Equals(KnownQueryParameterNames.ContinuationToken, StringComparison.OrdinalIgnoreCase));
                            searchParams.Add(Tuple.Create(KnownQueryParameterNames.ContinuationToken, ContinuationTokenEncoder.Encode(searchResult.ContinuationToken)));
                            prevContinuationToken = searchResult.ContinuationToken;
                        }
                        else
                        {
                            // We are at the end of the search results
                            return OperationCompleted;
                        }
                    }

                    // Run a search to get the first page of results
                    searchResult = await Search(definition, searchService, searchParams, cancellationToken);

                    // If the search result is empty, we can skip the rest of the processing
                    while ((searchResult?.Results != null && searchResult.Results.Any()) || !string.IsNullOrEmpty(prevContinuationToken))
                    {
                        // Store the continuation token for the next iteration
                        nextContinuationToken = searchResult.ContinuationToken;

                        // Enqueue the job for the current page of results
                        _logger.LogJobInformation(jobInfo, "Creating bulk update definition (3).");
                        var processingRecord = CreateProcessingDefinition(definition, searchService.Value, cancellationToken, definition.Type, prevContinuationToken, false);

                        _logger.LogJobInformation(jobInfo, "Enqueuing bulk update job (4).");
                        await _queueClient.EnqueueAsync(QueueType.BulkUpdate, cancellationToken, groupId: jobInfo.GroupId, definitions: processingRecord);

                        // Break if there are no more pages to process
                        if (string.IsNullOrEmpty(nextContinuationToken))
                        {
                            break;
                        }

                        // If there are more pages, update the previous continuation token to register the job for the next page
                        prevContinuationToken = nextContinuationToken;

                        // Get the next page of results using the continuation token
                        var cloneList = new List<Tuple<string, string>>(searchParams);
                        cloneList.Add(Tuple.Create(KnownQueryParameterNames.ContinuationToken, ContinuationTokenEncoder.Encode(nextContinuationToken)));
                        searchResult = await Search(definition, searchService, cloneList, cancellationToken);
                    }
                }
                else if (groupJobs.Count == 1)
                {
                    _logger.LogJobInformation(jobInfo, "Creating bulk update definition (5).");
                    var processingRecord = CreateProcessingDefinition(definition, searchService.Value, cancellationToken, definition.Type, null, true);
                    _logger.LogJobInformation(jobInfo, "Enqueuing bulk update job (5).");
                    await _queueClient.EnqueueAsync(QueueType.BulkUpdate, cancellationToken, groupId: jobInfo.GroupId, definitions: processingRecord);
                }

                return OperationCompleted;
            }
            finally
            {
                _contextAccessor.RequestContext = existingFhirRequestContext;
            }
        }

        internal virtual IFhirRequestContext CreateFhirRequestContext(BulkUpdateDefinition definition, JobInfo jobInfo)
        {
            return new FhirRequestContext(
                method: "BulkUpdate",
                uriString: definition.Url,
                baseUriString: definition.BaseUrl,
                correlationId: jobInfo.Id.ToString() + '-' + jobInfo.GroupId.ToString(),
                requestHeaders: new Dictionary<string, StringValues>(),
                responseHeaders: new Dictionary<string, StringValues>())
            {
                IsBackgroundTask = true,
            };
        }

        private static async Task<SearchResult> Search(BulkUpdateDefinition definition, IScoped<ISearchService> searchService, List<Tuple<string, string>> searchParams, CancellationToken cancellationToken)
        {
            return await searchService.Value.SearchAsync(
                definition.Type,
                searchParams,
                cancellationToken,
                true,
                resourceVersionTypes: ResourceVersionType.Latest,
                onlyIds: true,
                isIncludesOperation: false);
        }

        internal static BulkUpdateDefinition CreateProcessingDefinition(BulkUpdateDefinition baseDefinition, ISearchService searchService, CancellationToken cancellationToken, string resourceType = null, string continuationToken = null, bool readNextPage = false, string startSurrogateId = null, string endSurrogateId = null, string globalStartSurrogateId = null, string globalEndSurrogateId = null)
        {
            var searchParameters = new List<Tuple<string, string>>()
                {
                    new Tuple<string, string>(KnownQueryParameterNames.Summary, "count"),
                };

            if (baseDefinition.SearchParameters != null)
            {
                searchParameters.AddRange(baseDefinition.SearchParameters);
            }

            var cloneList = new List<Tuple<string, string>>();
            if (baseDefinition.SearchParameters != null)
            {
                cloneList = baseDefinition.SearchParameters.ToList();
            }

            if (!string.IsNullOrEmpty(continuationToken))
            {
                cloneList.Add(Tuple.Create(KnownQueryParameterNames.ContinuationToken, ContinuationTokenEncoder.Encode(continuationToken)));
            }

            return new BulkUpdateDefinition(
                    JobType.BulkUpdateProcessing,
                    resourceType,
                    cloneList,
                    baseDefinition.Url,
                    baseDefinition.BaseUrl,
                    baseDefinition.ParentRequestId,
                    baseDefinition.Parameters,
                    baseDefinition.IsParallel,
                    readNextPage,
                    startSurrogateId,
                    endSurrogateId,
                    globalStartSurrogateId,
                    globalEndSurrogateId,
                    baseDefinition.MaximumNumberOfResourcesPerQuery);
        }
    }
}
