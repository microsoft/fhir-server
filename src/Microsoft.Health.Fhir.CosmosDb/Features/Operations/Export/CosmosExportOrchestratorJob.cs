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
        private const int DefaultMaxNumberOfFeedRangesPerJob = 100;

        private readonly IQueueClient _queueClient;
        private ISearchService _searchService;

        public CosmosExportOrchestratorJob(
            IQueueClient queueClient,
            ISearchService searchService)
        {
            _queueClient = EnsureArg.IsNotNull(queueClient, nameof(queueClient));
            _searchService = EnsureArg.IsNotNull(searchService, nameof(searchService));
        }

        internal int MaxNumberOfFeedRangesPerJob { get; set; } = DefaultMaxNumberOfFeedRangesPerJob;

        public async Task<string> ExecuteAsync(JobInfo jobInfo, IProgress<string> progress, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(jobInfo, nameof(jobInfo));
            EnsureArg.IsNotNull(progress, nameof(progress));

            var record = jobInfo.DeserializeDefinition<ExportJobRecord>();
            record.QueuedTime = jobInfo.CreateDate; // get record of truth

            var groupJobs = await _queueClient.GetJobByGroupIdAsync(QueueType.Export, jobInfo.GroupId, true, cancellationToken);

            // Parallel system level export is parallelized by resource type and CosmosDB physical partitions feed range.
            if (record.ExportType == ExportJobType.All && record.IsParallel && (record.Filters == null || record.Filters.Count == 0))
            {
                var resourceTypes = string.IsNullOrEmpty(record.ResourceType)
                                 ? (await _searchService.GetUsedResourceTypes(cancellationToken))
                                 : record.ResourceType.Split(',');

                var physicalPartitionFeedRanges = await _searchService.GetFeedRanges(cancellationToken);

                var enqueuedRangesByResourceType = groupJobs.Select(x => JsonConvert.DeserializeObject<ExportJobRecord>(x.Definition))
                                                    .Where(x => x.ResourceType is not null)
                                                    .GroupBy(x => x.ResourceType)
                                                    .ToDictionary(x => x.Key, x => x.Select(x => x.FeedRange).ToList());

                foreach (var resourceType in resourceTypes)
                {
                    var definitions = new List<string>();

                    // Skip any feed range/resource type combos that have already been queued.
                    // This scenario is when the coordinator is killed during queueing of export jobs.
                    var notEnqueued = enqueuedRangesByResourceType.TryGetValue(resourceType, out var enqueuedRangesThisResourceType) ?
                                            physicalPartitionFeedRanges.Where(x => !enqueuedRangesThisResourceType.Contains(x)) :
                                            physicalPartitionFeedRanges;

                    foreach (var partitionRange in notEnqueued)
                    {
                        var processingRecord = CreateExportRecord(
                                record,
                                jobInfo.GroupId,
                                resourceType: resourceType,
                                feedRange: partitionRange);

                        definitions.Add(JsonConvert.SerializeObject(processingRecord));

                        if (definitions.Count >= MaxNumberOfFeedRangesPerJob)
                        {
                            await _queueClient.EnqueueAsync((byte)QueueType.Export, definitions.ToArray(), jobInfo.GroupId, false, false, cancellationToken);
                            definitions.Clear();
                        }
                    }

                    if (definitions.Count > 0)
                    {
                        await _queueClient.EnqueueAsync((byte)QueueType.Export, definitions.ToArray(), jobInfo.GroupId, false, false, cancellationToken);
                    }
                }
            }
            else if (groupJobs.Count == 1)
            {
                var processingRecord = CreateExportRecord(record, jobInfo.GroupId);
                await _queueClient.EnqueueAsync(QueueType.Export, cancellationToken, groupId: jobInfo.GroupId, definitions: processingRecord);
            }

            record.Status = OperationStatus.Completed;
            return JsonConvert.SerializeObject(record);
        }

        private static ExportJobRecord CreateExportRecord(ExportJobRecord record, long groupId, string resourceType = null, string feedRange = null)
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
                        since: record.Since,
                        till: record.Till,
                        feedRange: feedRange,
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
