// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
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
    public class ExportOrchestratorJob : IJob
    {
        private const int DefaultPollingFrequencyInSeconds = 3;

        // private readonly TimeSpan _defaultLengthOfTimeSlice = TimeSpan.FromDays(1);

        private JobInfo _jobInfo;
        private IQueueClient _queueClient;
        private ISearchService _searchService;
        private ILogger<ExportOrchestratorJob> _logger;

        public ExportOrchestratorJob(
            JobInfo jobInfo,
            IQueueClient queueClient,
            ISearchService searchService,
            ILoggerFactory loggerFactory)
        {
            EnsureArg.IsNotNull(jobInfo, nameof(jobInfo));
            EnsureArg.IsNotNull(queueClient, nameof(queueClient));
            EnsureArg.IsNotNull(searchService, nameof(searchService));
            EnsureArg.IsNotNull(loggerFactory, nameof(loggerFactory));

            _jobInfo = jobInfo;
            _queueClient = queueClient;
            _searchService = searchService;
            _logger = loggerFactory.CreateLogger<ExportOrchestratorJob>();
        }

        public int PollingFrequencyInSeconds { get; set; } = DefaultPollingFrequencyInSeconds;

        public async Task<string> ExecuteAsync(IProgress<string> progress, CancellationToken cancellationToken)
        {
            // If the filter attribute is null and it isn't patient or group export...
            // Call the SQL stored procedure to get resource surogate ids based on start and end time
            // Make a batch of export processing jobs
            // Repeat if needed
            // else
            // Make one processing job for the entire export

            ExportJobRecord record = JsonConvert.DeserializeObject<ExportJobRecord>(_jobInfo.Definition);
            var groupJobs = await _queueClient.GetJobByGroupIdAsync((byte)QueueType.Export, _jobInfo.GroupId, false, cancellationToken);
            var count = 0;
            foreach (var job in groupJobs)
            {
                count++;
            }

            if (count == 1)
            {
                string[] definitions;
                if (record.ExportType != ExportJobType.All || !record.Parallel)
                {
                    var processingRecord = CreateExportRecord(record);
                    definitions = new string[] { JsonConvert.SerializeObject(processingRecord) };
                }
                else if (string.IsNullOrEmpty(record.ResourceType))
                {
                    // Since the GetDateTimeRange method needs a resource type, when one isn't provided the time range just needs to be split into equal buckets.
                    var definitionsList = new List<string>();
                    var tillTicks = record.Till.ToDateTimeOffset(
                        defaultMonth: 1,
                        defaultDaySelector: (year, month) => 1,
                        defaultHour: 0,
                        defaultMinute: 0,
                        defaultSecond: 0,
                        defaultFraction: 0.0000000m,
                        defaultUtcOffset: TimeSpan.Zero).Ticks;
                    var sinceTicks = record.Since.ToDateTimeOffset(
                        defaultMonth: 1,
                        defaultDaySelector: (year, month) => 1,
                        defaultHour: 0,
                        defaultMinute: 0,
                        defaultSecond: 0,
                        defaultFraction: 0.0000000m,
                        defaultUtcOffset: TimeSpan.Zero).Ticks;

                    var lengthOfRange = (tillTicks - sinceTicks) / 10;
                    ExportJobRecord processingRecord;
                    for (int i = 0; i < 9; i++)
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
                        sequence: 9,
                        since: new PartialDateTime(new DateTimeOffset(new DateTime(sinceTicks + (9 * lengthOfRange)))),
                        till: record.Till);
                    definitionsList.Add(JsonConvert.SerializeObject(processingRecord));

                    definitions = definitionsList.ToArray();
                }
                else
                {
                    var resourceTypes = record.ResourceType.Split(',');
                    var definitionsList = new List<string>();
                    foreach (var type in resourceTypes)
                    {
                        var till = record.Till.ToDateTimeOffset(
                            defaultMonth: 1,
                            defaultDaySelector: (year, month) => 1,
                            defaultHour: 0,
                            defaultMinute: 0,
                            defaultSecond: 0,
                            defaultFraction: 0.0000000m,
                            defaultUtcOffset: TimeSpan.Zero);
                        var since = record.Since.ToDateTimeOffset(
                            defaultMonth: 1,
                            defaultDaySelector: (year, month) => 1,
                            defaultHour: 0,
                            defaultMinute: 0,
                            defaultSecond: 0,
                            defaultFraction: 0.0000000m,
                            defaultUtcOffset: TimeSpan.Zero);

                        // var numberOfRanges = (int)((till.Ticks - since.Ticks) / _defaultLengthOfTimeSlice.Ticks) + 1;
                        var ranges = await _searchService.GetDateTimeRange(type, since.DateTime, till.DateTime, 10, cancellationToken);
                        var sequence = 0;
                        foreach (var range in ranges)
                        {
                            var processingRecord = CreateExportRecord(record, sequence: sequence, resourceType: type, since: new PartialDateTime(new DateTimeOffset(range.Item1)), till: new PartialDateTime(new DateTimeOffset(range.Item2)));
                            definitionsList.Add(JsonConvert.SerializeObject(processingRecord));
                            sequence++;
                        }
                    }

                    definitions = definitionsList.ToArray();
                }

                groupJobs = await _queueClient.EnqueueAsync((byte)QueueType.Export, definitions, _jobInfo.GroupId, false, false, cancellationToken);
            }

            // else if check that the number of processing jobs is as expected. If not fail the job, this isn't recoverable.

            bool allJobsComplete;
            do
            {
                allJobsComplete = true;
                foreach (var job in groupJobs)
                {
                    if (job.Id != _jobInfo.Id && (job.Status == JobStatus.Running || job.Status == JobStatus.Created))
                    {
                        allJobsComplete = false;
                        break;
                    }
                }

                await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
                groupJobs = await _queueClient.GetJobByGroupIdAsync((byte)QueueType.Export, _jobInfo.GroupId, false, cancellationToken);
            }
            while (!allJobsComplete);

            bool jobFailed = false;
            foreach (var job in groupJobs)
            {
                if (job.Id != _jobInfo.Id)
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
                            record.FailureDetails = new JobFailureDetails(record.FailureDetails.FailureReason + "\r\nProcessing job had no results", System.Net.HttpStatusCode.InternalServerError);
                        }

                        jobFailed = true;
                    }
                }
            }

            if (jobFailed)
            {
                throw new JobExecutionException(record.FailureDetails.FailureReason, record);
            }

            return JsonConvert.SerializeObject(record);
        }

        private static ExportJobRecord CreateExportRecord(ExportJobRecord record, int sequence = -1, string resourceType = null, PartialDateTime since = null, PartialDateTime till = null)
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
                        (int)JobType.ExportProcessing);
        }
    }
}
