// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.Model;
using Microsoft.Build.Framework;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Export;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.JobManagement;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Operations.Export
{
    [JobTypeId((int)JobType.ExportOrchestrator)]
    public class CosmosExportOrchestratorJob : IJob
    {
        private readonly IQueueClient _queueClient;
        private readonly ILogger<CosmosExportOrchestratorJob> _logger;

        public CosmosExportOrchestratorJob(IQueueClient queueClient, ILogger<CosmosExportOrchestratorJob> logger)
        {
            _queueClient = EnsureArg.IsNotNull(queueClient, nameof(queueClient));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
        }

        public async Task<string> ExecuteAsync(JobInfo jobInfo, IProgress<string> progress, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(jobInfo, nameof(jobInfo));
            EnsureArg.IsNotNull(progress, nameof(progress));

            var record = jobInfo.DeserializeDefinition<ExportJobRecord>();
            record.QueuedTime = jobInfo.CreateDate; // get record of truth

            _logger.LogJobInformation(jobInfo, "Loading job by Group Id.");
            var groupJobs = await _queueClient.GetJobByGroupIdAsync(QueueType.Export, jobInfo.GroupId, true, cancellationToken);

            if (groupJobs.Count == 1)
            {
                _logger.LogJobInformation(jobInfo, "Creating export record.");
                var processingRecord = CreateExportRecord(record, jobInfo.GroupId);

                _logger.LogJobInformation(jobInfo, "Enqueuing export job.");
                await _queueClient.EnqueueAsync(QueueType.Export, cancellationToken, groupId: jobInfo.GroupId, definitions: processingRecord);
            }

            record.Status = OperationStatus.Completed;
            return JsonConvert.SerializeObject(record);
        }

        private static ExportJobRecord CreateExportRecord(ExportJobRecord record, long groupId)
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
                        record.ResourceType,
                        record.Filters,
                        record.Hash,
                        record.RollingFileSizeInMB,
                        record.RequestorClaims,
                        record.Since,
                        record.Till,
                        startSurrogateId: null,
                        endSurrogateId: null,
                        globalStartSurrogateId: null,
                        globalEndSurrogateId: null,
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
                        record.IncludeHistory,
                        record.IncludeDeleted,
                        record.SchemaVersion,
                        (int)JobType.ExportProcessing,
                        record.SmartRequest);

            rec.Id = string.Empty;
            rec.QueuedTime = record.QueuedTime; // preserve create date of coordinator job in form of queued time for all children, so same time is used on file names.

            return rec;
        }
    }
}
