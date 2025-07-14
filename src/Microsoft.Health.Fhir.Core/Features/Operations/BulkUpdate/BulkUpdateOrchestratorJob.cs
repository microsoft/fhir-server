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

                var fhirRequestContext = CreateFhirRequestContext(definition, jobInfo);

                _contextAccessor.RequestContext = fhirRequestContext;
                var surrogateIdRangeSize = (int)definition.MaximumNumberOfResourcesPerQuery;

                _logger.LogJobInformation(jobInfo, "Loading job by Group Id.");
                var groupJobs = await _queueClient.GetJobByGroupIdAsync(QueueType.BulkUpdate, jobInfo.GroupId, true, cancellationToken);

                // For Parallel bulk update, when there are no SearchParameters then create sub jobs by resourceType-surrogateId ranges
                using var searchService = _searchService.Invoke();
                if (definition.IsParallel && (definition.SearchParameters is null || !definition.SearchParameters.Any()))
                {
                    _logger.LogInformation("Creating bulk update subjobs by resourceType-surrogateId ranges.");
                    var resourceTypes = string.IsNullOrEmpty(definition.Type)
                          ? (await searchService.Value.GetUsedResourceTypes(cancellationToken))
                          : definition.Type.Split(',');
                    resourceTypes = resourceTypes.Where(x => !_excludedResourceTypes.Contains(x)).ToList();
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
                            }
                        }
                    });
                }
                else if (definition.IsParallel)
                {
                    // For Parallel bulk update, when there are SearchParameters then create sub jobs at continuation token level for matched and included resources.
                    string nextContinuationToken = null;
                    string prevContinuationToken = null;
                    var definitions = new List<BulkUpdateDefinition>();
                    var searchParams = definition.SearchParameters?.ToList() ?? new List<Tuple<string, string>>();

                    // Run a search to get the first page of results
                    SearchResult searchResult = await Search(definition, searchService, searchParams, false, cancellationToken);

                    // If the search result is empty, we can skip the rest of the processing
                    while ((searchResult?.Results != null && searchResult.Results.Any()) || !string.IsNullOrEmpty(prevContinuationToken))
                    {
                        // Store the continuation token for the next iteration
                        nextContinuationToken = searchResult.ContinuationToken;

                        // Enqueue the job for the current page of results
                        _logger.LogJobInformation(jobInfo, "Creating bulk update definition (3).");
                        var processingRecord = CreateProcessingDefinition(definition, searchService.Value, cancellationToken, definition.Type, prevContinuationToken, null, false);
                        definitions.Add(processingRecord);

                        // Check if includes continuation token is present, if so, we need to read next page for includes and enqueue the job with includes continuation token
                        if (searchResult.IncludesContinuationToken is not null && AreIncludeResultsTruncated())
                        {
                            SearchResult searchResultIncludes;
                            string currentIncludesContinuationToken = searchResult.IncludesContinuationToken;

                            // With do-while we register the job first for included results on the next page and then read that next page of included results to see if there are more
                            do
                            {
                                // Since this page contains Include token, let's first register the job for the next page of included results
                                _logger.LogJobInformation(jobInfo, "Creating bulk update definition (4).");
                                processingRecord = CreateProcessingDefinition(definition, searchService.Value, cancellationToken, definition.Type, prevContinuationToken, currentIncludesContinuationToken, false);
                                definitions.Add(processingRecord);

                                // For the first time if there are included results this will not be null
                                if (string.IsNullOrEmpty(currentIncludesContinuationToken))
                                {
                                    break;
                                }

                                // Search the next page by creating a new clone list for prevContinuationToken and currentIncludesContinuationToken
                                var cloneListForInclude = new List<Tuple<string, string>>(searchParams);
                                cloneListForInclude.Add(Tuple.Create(KnownQueryParameterNames.ContinuationToken, ContinuationTokenEncoder.Encode(prevContinuationToken)));
                                cloneListForInclude.Add(Tuple.Create(KnownQueryParameterNames.IncludesContinuationToken, ContinuationTokenEncoder.Encode(currentIncludesContinuationToken)));

                                // Run a search to get more included results (i.e. next-next page of includes)
                                searchResultIncludes = await Search(definition, searchService, cloneListForInclude, true, cancellationToken);
                                currentIncludesContinuationToken = searchResultIncludes.IncludesContinuationToken;
                            }
                            while (!string.IsNullOrEmpty(currentIncludesContinuationToken)); // Break when there are no more included results to process
                        }

                        // Break if there are no more pages to process
                        if (string.IsNullOrEmpty(nextContinuationToken))
                        {
                            break;
                        }

                        // If there re more pages, update the previous continuation token to register the job for the next page
                        prevContinuationToken = nextContinuationToken;

                        // Get the next page of results using the continuation token
                        var cloneList = new List<Tuple<string, string>>(searchParams);
                        cloneList.Add(Tuple.Create(KnownQueryParameterNames.ContinuationToken, ContinuationTokenEncoder.Encode(nextContinuationToken)));
                        searchResult = await Search(definition, searchService, cloneList, false, cancellationToken);
                    }

                    if (definitions.Any())
                    {
                        _logger.LogJobInformation(jobInfo, "Enqueuing bulk update job (4).");
                        await _queueClient.EnqueueAsync(QueueType.BulkUpdate, cancellationToken, groupId: jobInfo.GroupId, definitions: definitions.ToArray());
                    }
                }
                else if (groupJobs.Count == 1)
                {
                    _logger.LogJobInformation(jobInfo, "Creating bulk update definition (5).");
                    var processingRecord = CreateProcessingDefinition(definition, searchService.Value, cancellationToken, definition.Type, null, null, true);
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

        private static async Task<SearchResult> Search(BulkUpdateDefinition definition, IScoped<ISearchService> searchService, List<Tuple<string, string>> searchParams, bool isIncludesOperation, CancellationToken cancellationToken)
        {
            return await searchService.Value.SearchAsync(
                definition.Type,
                searchParams,
                cancellationToken,
                false,
                resourceVersionTypes: ResourceVersionType.Latest,
                onlyIds: true,
                isIncludesOperation: isIncludesOperation);
        }

        private bool AreIncludeResultsTruncated()
        {
            return _contextAccessor.RequestContext.BundleIssues.Any(
                x => string.Equals(x.Diagnostics, Core.Resources.TruncatedIncludeMessage, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(x.Diagnostics, Core.Resources.TruncatedIncludeMessageForIncludes, StringComparison.OrdinalIgnoreCase));
        }

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

            var cloneList = new List<Tuple<string, string>>();
            if (baseDefinition.SearchParameters != null)
            {
                cloneList = baseDefinition.SearchParameters.ToList();
            }

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
