// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
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
    public class CosmosExportOrchestratorJob : IJob
    {
        private const int DefaultNumberOfTargetJobsToQueue = 100;

        private readonly IQueueClient _queueClient;
        private ISearchService _searchService;

        public CosmosExportOrchestratorJob(
            IQueueClient queueClient,
            ISearchService searchService)
        {
            _queueClient = EnsureArg.IsNotNull(queueClient, nameof(queueClient));
            _searchService = EnsureArg.IsNotNull(searchService, nameof(searchService));
        }

        internal int TargetNumberOfJobToQueue { get; set; } = DefaultNumberOfTargetJobsToQueue;

        public async Task<string> ExecuteAsync(JobInfo jobInfo, IProgress<string> progress, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(jobInfo, nameof(jobInfo));
            EnsureArg.IsNotNull(progress, nameof(progress));

            var record = JsonConvert.DeserializeObject<ExportJobRecord>(jobInfo.Definition);
            record.QueuedTime = jobInfo.CreateDate; // get record of truth
            var groupJobs = await _queueClient.GetJobByGroupIdAsync((byte)QueueType.Export, jobInfo.GroupId, true, cancellationToken);

            // for parallel case we enqueue in batches, so we should handle not completed registration
            if (record.ExportType == ExportJobType.All && record.IsParallel && (record.Filters == null || record.Filters.Count == 0))
            {
                var atLeastOneWorkerJobRegistered = false;
                var targetRecordsPerQuery = (int)record.MaximumNumberOfResourcesPerQuery;

                var resourceTypes = string.IsNullOrEmpty(record.ResourceType)
                                  ? (await _searchService.GetUsedResourceTypes(cancellationToken))
                                  : record.ResourceType.Split(',');
                resourceTypes = resourceTypes.OrderByDescending(x => string.Equals(x, "Observation", StringComparison.OrdinalIgnoreCase)).ToList(); // true first, so observation is processed as soon as

                var since = record.Since == null ? new PartialDateTime(DateTime.MinValue).ToDateTimeOffset() : record.Since.ToDateTimeOffset();

                var globalStartId = since;
                var till = record.Till.ToDateTimeOffset();
                var globalEndId = till.DateTime.AddTicks(-1); // -1 is so _till value can be used as _since in the next time based export

                // Get the max end surrogate id for each resource type
                var enqueued = groupJobs.Where(x => x.Id != jobInfo.Id) // exclude coord
                                        .Select(x => JsonConvert.DeserializeObject<ExportJobRecord>(x.Definition))
                                        .Where(x => x.EndSurrogateId != null) // This is to handle current mock tests. It is not needed but does not hurt.
                                        .GroupBy(x => x.ResourceType)
                                        .ToDictionary(x => x.Key, x => x.Max(r => PartialDateTime.Parse(r.EndSurrogateId)));

                await Parallel.ForEachAsync(resourceTypes, new ParallelOptions { MaxDegreeOfParallelism = 1, CancellationToken = cancellationToken }, async (type, cancel) =>
                {
                    var startId = globalStartId;
                    if (enqueued.TryGetValue(type, out var max))
                    {
                        startId = max.ToDateTimeOffset().AddTicks(1);
                    }

                    var definitions = new List<string>();
                    uint rows = 0;

                    var ranges = _searchService.GetApproximateRecordCountDateTimeRanges(type, startId, globalEndId, targetRecordsPerQuery * TargetNumberOfJobToQueue, cancel);
                    await foreach (var range in ranges)
                    {
                        if (range.EndDateTime > startId)
                        {
                            startId = range.EndDateTime.AddTicks(1);
                        }

                        var processingRecord = CreateExportRecord(record, jobInfo.GroupId, resourceType: type, startSurrogateId: range.StartDateTime, endSurrogateId: range.EndDateTime, globalStartSurrogateId: globalStartId, globalEndSurrogateId: globalEndId);

                        if (rows + range.Count > (targetRecordsPerQuery * TargetNumberOfJobToQueue))
                        {
                            await _queueClient.EnqueueAsync((byte)QueueType.Export, definitions.ToArray(), jobInfo.GroupId, false, false, cancel);
                            atLeastOneWorkerJobRegistered = true;
                            definitions.Clear();
                            rows = 0;
                        }

                        if (range.Count > 0)
                        {
                            definitions.Add(JsonConvert.SerializeObject(processingRecord));
                            rows += range.Count;
                        }
                    }
                });

                if (!atLeastOneWorkerJobRegistered)
                {
                    var processingRecord = CreateExportRecord(record, jobInfo.GroupId);
                    await _queueClient.EnqueueAsync((byte)QueueType.Export, new[] { JsonConvert.SerializeObject(processingRecord) }, jobInfo.GroupId, false, false, cancellationToken);
                }
            }
            else if (groupJobs.Count == 1)
            {
                var processingRecord = CreateExportRecord(record, jobInfo.GroupId);
                await _queueClient.EnqueueAsync((byte)QueueType.Export, new[] { JsonConvert.SerializeObject(processingRecord) }, jobInfo.GroupId, false, false, cancellationToken);
            }

            record.Status = OperationStatus.Completed;
            return JsonConvert.SerializeObject(record);
        }

        private static ExportJobRecord CreateExportRecord(ExportJobRecord record, long groupId, string resourceType = null, DateTimeOffset? since = null, DateTimeOffset? till = null, DateTimeOffset? startSurrogateId = null, DateTimeOffset? endSurrogateId = null, DateTimeOffset? globalStartSurrogateId = null, DateTimeOffset? globalEndSurrogateId = null)
        {
            var format = $"{ExportFormatTags.ResourceName}-{ExportFormatTags.Id}";
            var container = record.StorageAccountContainerName;

            if (record.Id != record.StorageAccountContainerName)
            {
                format = $"{ExportFormatTags.Timestamp}-{groupId}/{format}";
            }
            else
            {
                // Need the export- to make sure the container meets the minimum length requirements of 3 characters.
                container = $"export-{groupId}";
            }

            // #TODO - not sure on storing surrogateIds for the Cosmos export - should we just store the since and till for the child jobs?
            // Needs research on how the ExportJob uses since and till.
            var rec = new ExportJobRecord(
                        record.RequestUri,
                        record.ExportType,
                        format,
                        string.IsNullOrEmpty(resourceType) ? record.ResourceType : resourceType,
                        record.Filters,
                        record.Hash,
                        record.RollingFileSizeInMB,
                        record.RequestorClaims,
                        since != null ? new PartialDateTime(since.Value) : record.Since,
                        till != null ? new PartialDateTime(till.Value) : record.Till,
                        startSurrogateId != null ? new PartialDateTime(startSurrogateId.Value).ToString() : null,
                        endSurrogateId != null ? new PartialDateTime(endSurrogateId.Value).ToString() : null,
                        globalStartSurrogateId != null ? new PartialDateTime(globalStartSurrogateId.Value).ToString() : null,
                        globalEndSurrogateId != null ? new PartialDateTime(globalEndSurrogateId.Value).ToString() : null,
                        record.GroupId,
                        record.StorageAccountConnectionHash,
                        record.StorageAccountUri,
                        record.AnonymizationConfigurationCollectionReference,
                        record.AnonymizationConfigurationLocation,
                        record.AnonymizationConfigurationFileETag,
                        record.MaximumNumberOfResourcesPerQuery,
                        record.NumberOfPagesPerCommit,
                        container,
                        record.IsParallel,
                        record.SchemaVersion,
                        (int)JobType.ExportProcessing,
                        record.SmartRequest);
            rec.Id = string.Empty;
            rec.QueuedTime = record.QueuedTime; // preserve create date of coordinator job in form of queued time for all children, so same time is used on file names.
            return rec;
        }
    }
}
