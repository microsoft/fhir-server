// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
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
                /*
                IReadOnlyList<string> resourceTypes = string.IsNullOrEmpty(record.ResourceType)
                                 ? (await _searchService.GetUsedResourceTypesWithCount(cancellationToken))
                                    .OrderByDescending(x => x.Count)
                                    .Select(x => x.ResourceType)
                                    .ToList()
                                 : record.ResourceType.Split(',');
                */

                IReadOnlyList<string> resourceTypes = string.IsNullOrEmpty(record.ResourceType)
                                 ? (await _searchService.GetUsedResourceTypes(cancellationToken))
                                 : record.ResourceType.Split(',');

                var ranges = await _searchService.GetFeedRanges(cancellationToken);

                foreach (var resourceType in resourceTypes)
                {
                    var definitions = new List<string>();

                    foreach (var feedRange in ranges)
                    {
                        var processingRecord = CreateExportRecord(
                                record,
                                jobInfo.GroupId,
                                resourceType: resourceType,
                                feedRange: feedRange);

                        definitions.Add(JsonConvert.SerializeObject(processingRecord));

                        if (definitions.Count >= DefaultNumberOfTimeRangesPerJob)
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
                await _queueClient.EnqueueAsync((byte)QueueType.Export, new[] { JsonConvert.SerializeObject(processingRecord) }, jobInfo.GroupId, false, false, cancellationToken);
            }

            record.Status = OperationStatus.Completed;
            return JsonConvert.SerializeObject(record);
        }

        private static ExportJobRecord CreateExportRecord(ExportJobRecord record, long groupId, string resourceType = null, PartialDateTime since = null, PartialDateTime till = null, string startSurrogateId = null, string endSurrogateId = null, string feedRange = null)
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
                        startSurrogateId: startSurrogateId,
                        endSurrogateId: endSurrogateId,
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
