// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.JobManagement;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Export
{
    [JobTypeId((int)JobType.ExportOrchestrator)]
    public class ExportOrchestratorJob : IJob
    {
        private const double DefaultPollingIntervalSec = 60;
        private const int DefaultSurrogateIdRangeSize = 100000;
        private const int DefaultNumberOfSurrogateIdRanges = 100;

        private IQueueClient _queueClient;
        private ISearchService _searchService;
        private ILogger<ExportOrchestratorJob> _logger; // TODO: Either remove or use

        public ExportOrchestratorJob(
            IQueueClient queueClient,
            ISearchService searchService,
            ILoggerFactory loggerFactory)
        {
            EnsureArg.IsNotNull(queueClient, nameof(queueClient));
            EnsureArg.IsNotNull(searchService, nameof(searchService));
            EnsureArg.IsNotNull(loggerFactory, nameof(loggerFactory));

            _queueClient = queueClient;
            _searchService = searchService;
            _logger = loggerFactory.CreateLogger<ExportOrchestratorJob>();
        }

        internal double PollingIntervalSec { get; set; } = DefaultPollingIntervalSec;

        internal int SurrogateIdRangeSize { get; set; } = DefaultSurrogateIdRangeSize;

        internal int NumberOfSurrogateIdRanges { get; set; } = DefaultNumberOfSurrogateIdRanges;

        internal bool RaiseTestException { get; set; } = false;

        public async Task<string> ExecuteAsync(JobInfo jobInfo, IProgress<string> progress, CancellationToken cancellationToken)
        {
            ExportJobRecord record = JsonConvert.DeserializeObject<ExportJobRecord>(jobInfo.Definition);
            var groupJobs = (await _queueClient.GetJobByGroupIdAsync((byte)QueueType.Export, jobInfo.GroupId, true, cancellationToken)).ToList();

            // for parallel case we enqueue in batches, so we should handle not completed registration
            if (record.ExportType == ExportJobType.All && record.IsParallel && (record.Filters == null || record.Filters.Count == 0))
            {
                var resourceTypes = string.IsNullOrEmpty(record.ResourceType)
                                  ? (await _searchService.GetUsedResourceTypes(cancellationToken)).Select(_ => _.Name)
                                  : record.ResourceType.Split(',');

                var since = record.Since == null ? new PartialDateTime(DateTime.MinValue).ToDateTimeOffset() : record.Since.ToDateTimeOffset();
                var globalStartId = _searchService.GetSurrogateId(since.DateTime);
                var till = record.Till.ToDateTimeOffset();
                var globalEndId = _searchService.GetSurrogateId(till.DateTime);

                var enqueued = groupJobs.Where(_ => _.Id != jobInfo.Id) // exclude coord
                                        .Select(_ => JsonConvert.DeserializeObject<ExportJobRecord>(_.Definition))
                                        .Where(_ => _.EndSurrogateId != null) // This is to handle current mock tests. It is not needed but does not hurt.
                                        .GroupBy(_ => _.ResourceType)
                                        .ToDictionary(_ => _.Key, _ => Tuple.Create(_.Max(r => GetSequence(r)), _.Max(r => long.Parse(r.EndSurrogateId))));

                foreach (var type in resourceTypes)
                {
                    var startId = globalStartId;
                    var sequence = 0;
                    if (enqueued.TryGetValue(type, out var max))
                    {
                        sequence = max.Item1 + 1;
                        startId = max.Item2 + 1;
                    }

                    var rows = 1;
                    while (rows > 0)
                    {
                        var definitions = new List<string>();
                        var ranges = await _searchService.GetSurrogateIdRanges(type, startId, globalEndId, SurrogateIdRangeSize, NumberOfSurrogateIdRanges, cancellationToken);
                        foreach (var range in ranges)
                        {
                            if (range.EndId > startId)
                            {
                                startId = range.EndId;
                            }

                            var processingRecord = CreateExportRecord(record, sequence: sequence, resourceType: type, startSurrogateId: range.StartId.ToString(), endSurrogateId: range.EndId.ToString(), globalStartSurrogateId: globalStartId.ToString(), globalEndSurrogateId: globalEndId.ToString());
                            definitions.Add(JsonConvert.SerializeObject(processingRecord));
                            sequence++;
                        }

                        startId++; // make sure we do not intersect ranges

                        rows = definitions.Count;
                        if (rows > 0)
                        {
                            await _queueClient.EnqueueAsync((byte)QueueType.Export, definitions.ToArray(), jobInfo.GroupId, false, false, cancellationToken);
                        }

                        if (RaiseTestException)
                        {
                            throw new ArgumentException("Test");
                        }
                    }
                }
            }
            else if (groupJobs.Count == 1)
            {
                var processingRecord = CreateExportRecord(record);
                await _queueClient.EnqueueAsync((byte)QueueType.Export, new string[] { JsonConvert.SerializeObject(processingRecord) }, jobInfo.GroupId, false, false, cancellationToken);
            }

            groupJobs = (await _queueClient.GetJobByGroupIdAsync((byte)QueueType.Export, jobInfo.GroupId, true, cancellationToken)).ToList();

            bool allJobsComplete;
            do
            {
                allJobsComplete = true;
                foreach (var job in groupJobs)
                {
                    if (job.Id != jobInfo.Id && (job.Status == JobStatus.Running || job.Status == JobStatus.Created))
                    {
                        allJobsComplete = false;
                        break;
                    }
                }

                await Task.Delay(TimeSpan.FromSeconds(PollingIntervalSec), cancellationToken);
                groupJobs = (await _queueClient.GetJobByGroupIdAsync((byte)QueueType.Export, jobInfo.GroupId, false, cancellationToken)).ToList();
            }
            while (!allJobsComplete);

            bool jobFailed = false;
            foreach (var job in groupJobs)
            {
                if (job.Id != jobInfo.Id)
                {
                    if (!string.IsNullOrEmpty(job.Result) && !job.Result.Equals("null", StringComparison.OrdinalIgnoreCase))
                    {
                        var processResult = JsonConvert.DeserializeObject<ExportJobRecord>(job.Result);
                        foreach (var output in processResult.Output)
                        {
                            if (record.Output.TryGetValue(output.Key, out var exportFileInfos))
                            {
                                exportFileInfos.AddRange(output.Value);
                            }
                            else
                            {
                                record.Output.Add(output.Key, output.Value);
                            }
                        }

                        if (processResult.FailureDetails != null)
                        {
                            if (record.FailureDetails == null)
                            {
                                record.FailureDetails = processResult.FailureDetails;
                            }
                            else if (!processResult.FailureDetails.FailureReason.Equals(record.FailureDetails.FailureReason, StringComparison.OrdinalIgnoreCase))
                            {
                                record.FailureDetails = new JobFailureDetails(record.FailureDetails.FailureReason + "\r\n" + processResult.FailureDetails.FailureReason, record.FailureDetails.FailureStatusCode);
                            }

                            jobFailed = true;
                        }
                    }
                    else
                    {
                        if (record.FailureDetails == null)
                        {
                            record.FailureDetails = new JobFailureDetails("Processing job had no results", System.Net.HttpStatusCode.InternalServerError);
                        }
                        else
                        {
                            record.FailureDetails = new JobFailureDetails(record.FailureDetails.FailureReason + "\r\nProcessing job had no results", HttpStatusCode.InternalServerError);
                        }

                        jobFailed = true;
                    }
                }
            }

            if (jobFailed)
            {
                record.Status = OperationStatus.Failed;
                throw new JobExecutionException(record.FailureDetails.FailureReason, record);
            }

            record.Status = OperationStatus.Completed;
            return JsonConvert.SerializeObject(record);
        }

        private static int GetSequence(ExportJobRecord record)
        {
            var split = record.ExportFormat.Split("-");
            return split.Length > 1 ? int.Parse(split[1]) : -1;
        }

        private static ExportJobRecord CreateExportRecord(ExportJobRecord record, int sequence = -1, string resourceType = null, PartialDateTime since = null, PartialDateTime till = null, string startSurrogateId = null, string endSurrogateId = null, string globalStartSurrogateId = null, string globalEndSurrogateId = null)
        {
            return new ExportJobRecord(
                        record.RequestUri,
                        record.ExportType,
                        sequence == -1 ? record.ExportFormat : $"{record.ExportFormat}-{sequence}",
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
                        record.StorageAccountContainerName,
                        record.IsParallel,
                        record.SchemaVersion,
                        (int)JobType.ExportProcessing,
                        record.SmartRequest);
        }
    }
}
