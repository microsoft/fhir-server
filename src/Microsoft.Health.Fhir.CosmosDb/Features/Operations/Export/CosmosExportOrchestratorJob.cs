// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
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
        private readonly IQueueClient _queueClient;
        private ISearchService _searchService;

        public CosmosExportOrchestratorJob(
            IQueueClient queueClient,
            ISearchService searchService)
        {
            _queueClient = EnsureArg.IsNotNull(queueClient, nameof(queueClient));
            _searchService = EnsureArg.IsNotNull(searchService, nameof(searchService));
        }

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

                var resourceTypes = string.IsNullOrEmpty(record.ResourceType)
                                  ? (await _searchService.GetUsedResourceTypes(cancellationToken))
                                  : record.ResourceType.Split(',');
                resourceTypes = resourceTypes.OrderByDescending(x => string.Equals(x, "Observation", StringComparison.OrdinalIgnoreCase)).ToList(); // true first, so observation is processed as soon as

                var since = record.Since == null ? new PartialDateTime(DateTime.MinValue).ToDateTimeOffset() : record.Since.ToDateTimeOffset();
                var till = record.Till.ToDateTimeOffset();

                await Parallel.ForEachAsync(resourceTypes, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = cancellationToken }, async (type, cancel) =>
                {
                    var processingRecord = CreateExportRecord(record, jobInfo.GroupId, resourceType: type);
                    await _queueClient.EnqueueAsync((byte)QueueType.Export, new[] { JsonConvert.SerializeObject(processingRecord) }, jobInfo.GroupId, false, false, cancel);
                    atLeastOneWorkerJobRegistered = true;
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

        private static ExportJobRecord CreateExportRecord(ExportJobRecord record, long groupId, string resourceType = null, DateTimeOffset? since = null, DateTimeOffset? till = null)
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
                        null,
                        null,
                        null,
                        null,
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
