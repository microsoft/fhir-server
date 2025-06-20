// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
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
        private readonly List<string> _excludedResourceTypes = new() { "SearchParameter", "StructureDefinition" };

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

                var fhirRequestContext = new FhirRequestContext(
                    method: "BulkUpdate",
                    uriString: definition.Url,
                    baseUriString: definition.BaseUrl,
                    correlationId: jobInfo.Id.ToString() + '-' + jobInfo.GroupId.ToString(),
                    requestHeaders: new Dictionary<string, StringValues>(),
                    responseHeaders: new Dictionary<string, StringValues>())
                {
                    IsBackgroundTask = true,
                };

                _contextAccessor.RequestContext = fhirRequestContext;
                var surrogateIdRangeSize = (int)definition.MaximumNumberOfResourcesPerQuery;

                _logger.LogJobInformation(jobInfo, "Loading job by Group Id.");
                var groupJobs = await _queueClient.GetJobByGroupIdAsync(QueueType.BulkUpdate, jobInfo.GroupId, true, cancellationToken);

                // If system level Parallel bulk update then create sub jobs by resourceType-surrogateId ranges
                using var searchService = _searchService.Invoke();
                if (definition.IsParallel && !definition.SearchParameters.Any())
                {
                    var atLeastOneWorkerJobRegistered = false;

                    // For system level bulk update, get all the resource types that are used in the system and enqueue jobs for each resource type-surrogateId range combination.
                    // For ResourceType based bulk updates query with includes and revinclude could return multiple resourceTypes so we will do the generic search without surrogateId ranges
                    var resourceTypes = await searchService.Value.GetUsedResourceTypes(cancellationToken);
                    resourceTypes = resourceTypes
                        .Where(x => !_excludedResourceTypes.Contains(x))
                        .ToList();

                    var globalStartId = new PartialDateTime(DateTime.MinValue).ToDateTimeOffset().ToId();
                    var globalEndId = new PartialDateTime(jobInfo.CreateDate).ToDateTimeOffset().ToId() - 1; // -1 is so _till value can be used as _since in the next time based export

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
                                atLeastOneWorkerJobRegistered = true;
                            }
                        }
                    });

                    if (!atLeastOneWorkerJobRegistered)
                    {
                        _logger.LogJobInformation(jobInfo, "Creating bulk update definition (2).");
                        var processingRecord = CreateProcessingDefinition(definition, searchService.Value, cancellationToken, definition.Type, null, null, true);

                        _logger.LogJobInformation(jobInfo, "Enqueuing bulk update job (2).");

                        await _queueClient.EnqueueAsync(QueueType.BulkUpdate, cancellationToken, groupId: jobInfo.GroupId, definitions: processingRecord);
                    }
                }
                else if (definition.IsParallel)
                {
                    // read all the data from search using Continuation token and create processing definitions and enqueue them by storing continuation token as well
                    string nextContinuationToken;
                    string prevContinuationToken = null;
                    var searchParams = definition.SearchParameters.ToList();
                    SearchResult searchResult = await searchService.Value.SearchAsync(
                        definition.Type,
                        searchParams,
                        cancellationToken,
                        false,
                        resourceVersionTypes: ResourceVersionType.Latest,
                        onlyIds: true);

                    while ((searchResult?.Results != null && searchResult.Results.Any()) || !string.IsNullOrEmpty(prevContinuationToken))
                    {
                        nextContinuationToken = searchResult.ContinuationToken;

                        _logger.LogJobInformation(jobInfo, "Creating bulk update definition (3).");
                        var processingRecord = CreateProcessingDefinition(definition, searchService.Value, cancellationToken, definition.Type, prevContinuationToken, null, false);

                        _logger.LogJobInformation(jobInfo, "Enqueuing bulk update job (3).");
                        await _queueClient.EnqueueAsync(QueueType.BulkUpdate, cancellationToken, groupId: jobInfo.GroupId, definitions: processingRecord);

                        // check if includes continuation token is present, if so, we need to read next page for includes and
                        // then enqueue the job with includes continuation token
                        if (searchResult.IncludesContinuationToken is not null)
                        {
                            SearchResult searchResultIncludes;
                            string currentIncludesContinuationToken = searchResult.IncludesContinuationToken;
                            do
                            {
                                // Since this page contains Include token, let's register the job for that page of included results
                                processingRecord = CreateProcessingDefinition(definition, searchService.Value, cancellationToken, definition.Type, nextContinuationToken, currentIncludesContinuationToken, false);
                                await _queueClient.EnqueueAsync(QueueType.BulkUpdate, cancellationToken, groupId: jobInfo.GroupId, definitions: processingRecord);

                                // Create a new clone list for ct and ict
                                var cloneListForInclude = new List<Tuple<string, string>>(searchParams)
                                {
                                    Tuple.Create(KnownQueryParameterNames.ContinuationToken, nextContinuationToken),
                                    Tuple.Create(KnownQueryParameterNames.IncludesContinuationToken, currentIncludesContinuationToken),
                                };

                                // Run a search to see if there are more included results
                                searchResultIncludes = await searchService.Value.SearchAsync(
                                definition.Type,
                                cloneListForInclude,
                                cancellationToken,
                                false,
                                resourceVersionTypes: ResourceVersionType.Latest,
                                onlyIds: true,
                                isIncludesOperation: true);

                                currentIncludesContinuationToken = searchResultIncludes.ContinuationToken;
                            }
                            while (!string.IsNullOrEmpty(currentIncludesContinuationToken));
                        }

                        if (nextContinuationToken is null)
                        {
                            // No more results to process
                            break;
                        }

                        prevContinuationToken = nextContinuationToken;
                        var cloneList = new List<Tuple<string, string>>(searchParams)
                            {
                                Tuple.Create(KnownQueryParameterNames.ContinuationToken, nextContinuationToken),
                            };

                        searchResult = await searchService.Value.SearchAsync(
                            definition.Type,
                            cloneList,
                            cancellationToken,
                            false,
                            resourceVersionTypes: ResourceVersionType.Latest,
                            onlyIds: true);
                    }
                }
                else if (groupJobs.Count == 1)
                {
                    _logger.LogJobInformation(jobInfo, "Creating bulk update definition (4).");
                    var processingRecord = CreateProcessingDefinition(definition, searchService.Value, cancellationToken, definition.Type, null, null, true);

                    _logger.LogJobInformation(jobInfo, "Enqueuing bulk update job (4).");
                    await _queueClient.EnqueueAsync(QueueType.BulkUpdate, cancellationToken, groupId: jobInfo.GroupId, definitions: processingRecord);
                }

                return OperationCompleted;
            }
            finally
            {
                _contextAccessor.RequestContext = existingFhirRequestContext;
            }
        }

        // Creates a bulk update processing job.
        // Each processing job only updates one resource type based on the surrogate Id ranges if provided
        internal static BulkUpdateDefinition CreateProcessingDefinition(BulkUpdateDefinition baseDefinition, ISearchService searchService, CancellationToken cancellationToken, string resourceType = null, string continuationToken = null, string includesContinuationToken = null, bool readNextPage = false, string startSurrogateId = null, string endSurrogateId = null, string globalStartSurrogateId = null, string globalEndSurrogateId = null)
        {
            var searchParameters = new List<Tuple<string, string>>()
                {
                    new Tuple<string, string>(KnownQueryParameterNames.Summary, "count"),
                };

            if (baseDefinition.SearchParameters != null)
            {
                searchParameters.AddRange(baseDefinition.SearchParameters);
            }

            // run a search for included results
            var cloneList = new List<Tuple<string, string>>(baseDefinition.SearchParameters);

            if (!string.IsNullOrEmpty(continuationToken))
            {
                cloneList.Add(Tuple.Create(KnownQueryParameterNames.ContinuationToken, ContinuationTokenEncoder.Encode(continuationToken)));
            }

            if (!string.IsNullOrEmpty(includesContinuationToken))
            {
                cloneList.Add(Tuple.Create(KnownQueryParameterNames.IncludesContinuationToken, ContinuationTokenEncoder.Encode(includesContinuationToken)));
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
