// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Export;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.JobManagement;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Operations.Export
{
    [JobTypeId((int)JobType.ExportOrchestrator)]
    public class CosmosExportOrchestratorJob : ExportOrchestratorJob
    {
        private readonly IQueueClient _queueClient;
        private readonly Func<IScoped<ISearchService>> _searchServiceScopeFactory;
        private readonly ILogger<CosmosExportOrchestratorJob> _logger;

        public CosmosExportOrchestratorJob(
            IQueueClient queueClient,
            Func<IScoped<ISearchService>> searchServiceScopeFactory,
            ILogger<CosmosExportOrchestratorJob> logger)
        {
            _queueClient = EnsureArg.IsNotNull(queueClient, nameof(queueClient));
            _searchServiceScopeFactory = EnsureArg.IsNotNull(searchServiceScopeFactory, nameof(searchServiceScopeFactory));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
        }

        public override async Task<string> ExecuteAsync(JobInfo jobInfo, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(jobInfo, nameof(jobInfo));

            var record = ExtractJobRecord(jobInfo);

            _logger.LogJobInformation(jobInfo, "Loading job by Group Id.");
            var groupJobs = await _queueClient.GetJobByGroupIdAsync(QueueType.Export, jobInfo.GroupId, true, cancellationToken);

            // Parallel system level export is parallelized by resource type and CosmosDB physical partitions feed range.
            if (record.ExportType == ExportJobType.All && record.IsParallel && (record.Filters == null || record.Filters.Count == 0))
            {
                using var searchService = _searchServiceScopeFactory.Invoke();

                var resourceTypes = string.IsNullOrEmpty(record.ResourceType)
                                 ? (await searchService.Value.GetUsedResourceTypes(cancellationToken))
                                 : record.ResourceType.Split(',');

                var physicalPartitionFeedRanges = await searchService.Value.GetFeedRanges(cancellationToken);

                var enqueuedRangesByResourceType = groupJobs.Select(x => JsonConvert.DeserializeObject<ExportJobRecord>(x.Definition))
                                                    .Where(x => x.ResourceType is not null)
                                                    .GroupBy(x => x.ResourceType)
                                                    .ToDictionary(x => x.Key, x => x.Select(x => x.FeedRange).ToList());

                foreach (var resourceType in resourceTypes)
                {
                    // Skip any feed range/resource type combos that have already been queued.
                    // This scenario is when the coordinator is killed during queueing of export jobs.
                    var notEnqueued = enqueuedRangesByResourceType.TryGetValue(resourceType, out var enqueuedRangesThisResourceType) ?
                                            physicalPartitionFeedRanges.Where(x => !enqueuedRangesThisResourceType.Contains(x)) :
                                            physicalPartitionFeedRanges;

                    foreach (var partitionRange in notEnqueued)
                    {
                        _logger.LogJobInformation(jobInfo, "Creating export record (1).");

                        var processingRecord = CreateExportRecord(
                                record,
                                jobInfo.GroupId,
                                resourceType: resourceType,
                                feedRange: partitionRange);

                        string[] definitions = [JsonConvert.SerializeObject(processingRecord)];

                        _logger.LogJobInformation(jobInfo, "Enqueuing export job (1).");
                        await _queueClient.EnqueueAsync((byte)QueueType.Export, definitions, jobInfo.GroupId, false, cancellationToken);
                    }
                }
            }
            else if (groupJobs.Count == 1)
            {
                _logger.LogJobInformation(jobInfo, "Creating export record (2).");
                var processingRecord = CreateExportRecord(record, jobInfo.GroupId);

                _logger.LogJobInformation(jobInfo, "Enqueuing export job (2).");
                await _queueClient.EnqueueAsync(QueueType.Export, cancellationToken, groupId: jobInfo.GroupId, definitions: processingRecord);
            }

            record.Status = OperationStatus.Completed;
            return JsonConvert.SerializeObject(record);
        }
    }
}
