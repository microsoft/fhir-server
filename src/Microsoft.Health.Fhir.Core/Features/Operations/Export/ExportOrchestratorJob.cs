// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
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
        private const int DefaultPollingFrequencyInSeconds = 3;
        private const int DefaultNumberOfParallelJobs = 10;

        private IQueueClient _queueClient;
        private ISearchService _searchService;
        private ILogger<ExportOrchestratorJob> _logger;

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

        public int PollingFrequencyInSeconds { get; set; } = DefaultPollingFrequencyInSeconds;

        public int NumberOfParallelJobs { get; set; } = DefaultNumberOfParallelJobs;

        public async Task<string> ExecuteAsync(JobInfo jobInfo, IProgress<string> progress, CancellationToken cancellationToken)
        {
            ExportJobRecord record = JsonConvert.DeserializeObject<ExportJobRecord>(jobInfo.Definition);
            var groupJobs = await _queueClient.GetJobByGroupIdAsync((byte)QueueType.Export, jobInfo.GroupId, false, cancellationToken);
            int numberOfParallelJobs = record.Parallel > 0 ? record.Parallel : NumberOfParallelJobs;
            var count = 0;
            foreach (var job in groupJobs)
            {
                count++;
            }

            if (count == 1)
            {
                string[] definitions;

                if (record.ExportType != ExportJobType.All || record.Parallel == 1 || record.Since == null)
                {
                    var processingRecord = CreateExportRecord(record);
                    definitions = new string[] { JsonConvert.SerializeObject(processingRecord) };
                }
                else if (string.IsNullOrEmpty(record.ResourceType))
                {
                    // Since the GetDateTimeRange method needs a resource type, when one isn't provided the time range just needs to be split into equal buckets.
                    var definitionsList = new List<string>();
                    var tillTicks = record.Till.ToDateTimeOffset().Ticks;
                    var sinceTicks = record.Since.ToDateTimeOffset().Ticks;

                    var lengthOfRange = (tillTicks - sinceTicks) / numberOfParallelJobs;
                    ExportJobRecord processingRecord;
                    for (int i = 0; i < (numberOfParallelJobs - 1); i++)
                    {
                        processingRecord = CreateExportRecord(
                            record,
                            sequence: i,
                            since: new PartialDateTime(new DateTimeOffset(new DateTime(sinceTicks + (i * lengthOfRange)))),
                            till: new PartialDateTime(new DateTimeOffset(new DateTime(sinceTicks + ((i + 1) * lengthOfRange)))));
                        definitionsList.Add(JsonConvert.SerializeObject(processingRecord));
                    }

                    processingRecord = CreateExportRecord(
                        record,
                        sequence: numberOfParallelJobs - 1,
                        since: new PartialDateTime(new DateTimeOffset(new DateTime(sinceTicks + ((numberOfParallelJobs - 1) * lengthOfRange)))),
                        till: record.Till);
                    definitionsList.Add(JsonConvert.SerializeObject(processingRecord));

                    definitions = definitionsList.ToArray();
                }
                else
                {
                    var resourceTypes = record.ResourceType.Split(',');
                    var definitionsList = new List<string>();

                    var till = record.Till.ToDateTimeOffset();
                    var since = record.Since.ToDateTimeOffset();

                    foreach (var type in resourceTypes)
                    {
                        var ranges = await _searchService.GetSurrogateIdRanges(type, since.DateTime, till.DateTime, numberOfParallelJobs, cancellationToken);
                        var sequence = 0;
                        foreach (var range in ranges)
                        {
                            var processingRecord = CreateExportRecord(record, sequence: sequence, resourceType: type, startSurrogateId: range.start.ToString(), endSurrogateId: range.end.ToString(), globalStartSurrogateId: range.globalStart.ToString(), globalEndSurrogateId: range.globalEnd.ToString());
                            definitionsList.Add(JsonConvert.SerializeObject(processingRecord));
                            sequence++;
                        }
                    }

                    definitions = definitionsList.ToArray();
                }

                groupJobs = await _queueClient.EnqueueAsync((byte)QueueType.Export, definitions, jobInfo.GroupId, false, false, cancellationToken);
            }

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

                await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
                groupJobs = await _queueClient.GetJobByGroupIdAsync((byte)QueueType.Export, jobInfo.GroupId, false, cancellationToken);
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
                        record.Parallel,
                        record.SchemaVersion,
                        (int)JobType.ExportProcessing,
                        record.SmartRequest);
        }
    }
}
