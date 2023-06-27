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
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Export;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.JobManagement;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.SqlServer.Features.Operations.Export
{
    [JobTypeId((int)JobType.ExportOrchestrator)]
    public class SqlExportOrchestratorJob : IJob
    {
        private const int DefaultNumberOfSurrogateIdRanges = 100;

        private IQueueClient _queueClient;
        private ISearchService _searchService;

        public SqlExportOrchestratorJob(
            IQueueClient queueClient,
            ISearchService searchService)
        {
            EnsureArg.IsNotNull(queueClient, nameof(queueClient));
            EnsureArg.IsNotNull(searchService, nameof(searchService));

            _queueClient = queueClient;
            _searchService = searchService;
        }

        internal int NumberOfSurrogateIdRanges { get; set; } = DefaultNumberOfSurrogateIdRanges;

        public async Task<string> ExecuteAsync(JobInfo jobInfo, IProgress<string> progress, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(jobInfo, nameof(jobInfo));
            EnsureArg.IsNotNull(progress, nameof(progress));

            var record = jobInfo.DeserializeDefinition<ExportJobRecord>();
            record.QueuedTime = jobInfo.CreateDate; // get record of truth
            var surrogateIdRangeSize = (int)record.MaximumNumberOfResourcesPerQuery;
            var groupJobs = await _queueClient.GetJobByGroupIdAsync(QueueType.Export, jobInfo.GroupId, true, cancellationToken);

            // for parallel case we enqueue in batches, so we should handle not completed registration
            if (record.ExportType == ExportJobType.All && record.IsParallel && (record.Filters == null || record.Filters.Count == 0))
            {
                var atLeastOneWorkerJobRegistered = false;

                var resourceTypes = string.IsNullOrEmpty(record.ResourceType)
                                  ? (await _searchService.GetUsedResourceTypes(cancellationToken))
                                  : record.ResourceType.Split(',');
                resourceTypes = resourceTypes.OrderByDescending(x => string.Equals(x, "Observation", StringComparison.OrdinalIgnoreCase)).ToList(); // true first, so observation is processed as soon as

                var since = record.Since == null ? new PartialDateTime(DateTime.MinValue).ToDateTimeOffset() : record.Since.ToDateTimeOffset();

                var globalStartId = since.DateTime.DateToId();
                var till = record.Till.ToDateTimeOffset();
                var globalEndId = till.DateTime.DateToId() - 1; // -1 is so _till value can be used as _since in the next time based export

                var enqueued = groupJobs.Where(x => x.Id != jobInfo.Id) // exclude coord
                                        .Select(x => JsonConvert.DeserializeObject<ExportJobRecord>(x.Definition))
                                        .Where(x => x.EndSurrogateId != null) // This is to handle current mock tests. It is not needed but does not hurt.
                                        .GroupBy(x => x.ResourceType)
                                        .ToDictionary(x => x.Key, x => x.Max(r => long.Parse(r.EndSurrogateId)));

                await Parallel.ForEachAsync(resourceTypes, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = cancellationToken }, async (type, cancel) =>
                {
                    var startId = globalStartId;
                    if (enqueued.TryGetValue(type, out var max))
                    {
                        startId = max + 1;
                    }

                    var rows = 1;
                    while (rows > 0)
                    {
                        var definitions = new List<string>();
                        var ranges = await _searchService.GetSurrogateIdRanges(type, startId, globalEndId, surrogateIdRangeSize, NumberOfSurrogateIdRanges, true, cancel);
                        foreach (var range in ranges)
                        {
                            if (range.EndId > startId)
                            {
                                startId = range.EndId;
                            }

                            var processingRecord = CreateExportRecord(record, jobInfo.GroupId, resourceType: type, startSurrogateId: range.StartId.ToString(), endSurrogateId: range.EndId.ToString(), globalStartSurrogateId: globalStartId.ToString(), globalEndSurrogateId: globalEndId.ToString());
                            definitions.Add(JsonConvert.SerializeObject(processingRecord));
                        }

                        startId++; // make sure we do not intersect ranges

                        rows = definitions.Count;
                        if (rows > 0)
                        {
                            await _queueClient.EnqueueAsync(QueueType.Export, cancel, groupId: jobInfo.GroupId, definitions: definitions);
                            atLeastOneWorkerJobRegistered = true;
                        }
                    }
                });

                if (!atLeastOneWorkerJobRegistered)
                {
                    var processingRecord = CreateExportRecord(record, jobInfo.GroupId);
                    await _queueClient.EnqueueAsync(QueueType.Export, cancellationToken, groupId: jobInfo.GroupId, definitions: JsonConvert.SerializeObject(processingRecord));
                }
            }
            else if (groupJobs.Count == 1)
            {
                var processingRecord = CreateExportRecord(record, jobInfo.GroupId);
                await _queueClient.EnqueueAsync(QueueType.Export, cancellationToken, groupId: jobInfo.GroupId, definitions: JsonConvert.SerializeObject(processingRecord));
            }

            record.Status = OperationStatus.Completed;
            return JsonConvert.SerializeObject(record);
        }

        private static ExportJobRecord CreateExportRecord(ExportJobRecord record, long groupId, string resourceType = null, PartialDateTime since = null, PartialDateTime till = null, string startSurrogateId = null, string endSurrogateId = null, string globalStartSurrogateId = null, string globalEndSurrogateId = null)
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
                        record.RequestUri,
                        record.ExportType,
                        format,
                        string.IsNullOrEmpty(resourceType) ? record.ResourceType : resourceType,
                        record.Filters,
                        record.Hash,
                        record.RollingFileSizeInMB,
                        record.RequestorClaims,
                        since == null ? record.Since : since,
                        till == null ? record.Till : till,
                        startSurrogateId,
                        endSurrogateId,
                        globalStartSurrogateId,
                        globalEndSurrogateId,
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
