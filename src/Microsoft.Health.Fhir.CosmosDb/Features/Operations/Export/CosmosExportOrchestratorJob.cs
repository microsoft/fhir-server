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
        private const int DefaultNumberOfTimeRangesPerJob = 100;
        private const int DefaultParallelizationFactor = 8;

        private readonly IQueueClient _queueClient;
        private ISearchService _searchService;

        public CosmosExportOrchestratorJob(
            IQueueClient queueClient,
            ISearchService searchService)
        {
            _queueClient = EnsureArg.IsNotNull(queueClient, nameof(queueClient));
            _searchService = EnsureArg.IsNotNull(searchService, nameof(searchService));
        }

        internal int NumberOfTimeRangesPerJob { get; set; } = DefaultNumberOfTimeRangesPerJob;

        internal int ParallelizationFactor { get; set; } = DefaultParallelizationFactor;

        public async Task<string> ExecuteAsync(JobInfo jobInfo, IProgress<string> progress, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(jobInfo, nameof(jobInfo));
            EnsureArg.IsNotNull(progress, nameof(progress));

            var record = JsonConvert.DeserializeObject<ExportJobRecord>(jobInfo.Definition);
            record.QueuedTime = jobInfo.CreateDate; // get record of truth
            var timeRangeSize = (int)record.MaximumNumberOfResourcesPerQuery;
            var groupJobs = await _queueClient.GetJobByGroupIdAsync((byte)QueueType.Export, jobInfo.GroupId, true, cancellationToken);

            // for parallel case we enqueue in batches, so we should handle not completed registration
            if (record.ExportType == ExportJobType.All && record.IsParallel && (record.Filters == null || record.Filters.Count == 0))
            {
                var atLeastOneWorkerJobRegistered = false;

                IReadOnlyList<string> resourceTypes = string.IsNullOrEmpty(record.ResourceType)
                                 ? (await _searchService.GetUsedResourceTypesWithCount(cancellationToken))
                                    .OrderByDescending(x => x.Count)
                                    .Select(x => x.ResourceType)
                                    .ToList()
                                 : record.ResourceType.Split(',');

                var since = record.Since == null ? new PartialDateTime(DateTime.UnixEpoch).ToDateTimeOffset() : record.Since.ToDateTimeOffset();
                var till = record.Till.ToDateTimeOffset().AddTicks(-1); // -1 is so _till value can be used as _since in the next time based export

                // Find group jobs in flight in case orchestrator was restarted
                var enqueued = groupJobs.Where(x => x.Id != jobInfo.Id) // exclude main orchestrator
                                        .Where(x => x.GetJobTypeId() != (int)JobType.ExportOrchestrator)
                                        .Select(x => JsonConvert.DeserializeObject<ExportJobRecord>(x.Definition))
                                        .GroupBy(x => x.ResourceType).ToDictionary(x => x.Key, x => x.Max(r => r.EndTime));

                await Parallel.ForEachAsync(resourceTypes, new ParallelOptions { MaxDegreeOfParallelism = ParallelizationFactor, CancellationToken = cancellationToken }, async (type, cancel) =>
                {
                    var startTime = since;

                    // Reset the start time in case this jobs is resuming.
                    if (enqueued.TryGetValue(type, out var max) && max.HasValue)
                    {
                        startTime = max.Value.AddTicks(1);
                    }

                    var rows = 0;

                    do
                    {
                        var definitions = new List<string>();

                        // Find all the resource ranges to split export processing jobs into the specified rangeSize for performance.
                        var ranges = await _searchService.GetResourceTimeRanges(type, startTime, till, timeRangeSize, NumberOfTimeRangesPerJob, cancel);

                        foreach (var range in ranges)
                        {
                            var processingRecord = CreateExportRecord(
                                record,
                                jobInfo.GroupId,
                                resourceType: type,
                                since: new PartialDateTime(range.StartTime),
                                till: new PartialDateTime(range.EndTime));

                            definitions.Add(JsonConvert.SerializeObject(processingRecord));
                        }

                        if (ranges.Any())
                        {
                            startTime = ranges.Max(x => x.EndTime).AddTicks(1);
                        }

                        rows = definitions.Count;

                        if (rows > 0)
                        {
                            await _queueClient.EnqueueAsync((byte)QueueType.Export, definitions.ToArray(), jobInfo.GroupId, false, false, cancel);
                            atLeastOneWorkerJobRegistered = true;
                        }
                    }
                    while (rows > 0);
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

        private static ExportJobRecord CreateExportRecord(ExportJobRecord record, long groupId, string resourceType = null, PartialDateTime since = null, PartialDateTime till = null)
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

            var rec = new ExportJobRecord(
                        requestUri: record.RequestUri,
                        exportType: record.ExportType,
                        exportFormat: format,
                        resourceType: string.IsNullOrEmpty(resourceType) ? record.ResourceType : resourceType,
                        filters: record.Filters,
                        hash: record.Hash,
                        rollingFileSizeInMB: record.RollingFileSizeInMB,
                        requestorClaims: record.RequestorClaims,
                        since: since ?? record.Since,
                        till: till ?? record.Till,
                        groupId: record.GroupId,
                        storageAccountConnectionHash: record.StorageAccountConnectionHash,
                        storageAccountUri: record.StorageAccountUri,
                        anonymizationConfigurationCollectionReference: record.AnonymizationConfigurationCollectionReference,
                        anonymizationConfigurationLocation: record.AnonymizationConfigurationLocation,
                        anonymizationConfigurationFileETag: record.AnonymizationConfigurationFileETag,
                        maximumNumberOfResourcesPerQuery: record.MaximumNumberOfResourcesPerQuery,
                        numberOfPagesPerCommit: record.NumberOfPagesPerCommit,
                        storageAccountContainerName: container,
                        isParallel: record.IsParallel,
                        schemaVersion: record.SchemaVersion,
                        typeId: (int)JobType.ExportProcessing,
                        smartRequest: record.SmartRequest);
            rec.Id = string.Empty;
            rec.QueuedTime = record.QueuedTime; // preserve create date of coordinator job in form of queued time for all children, so same time is used on file names.
            return rec;
        }
    }
}
