// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Export;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.JobManagement;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Operations.Export
{
    [JobTypeId((int)JobType.ExportOrchestrator)]
    public class CosmosExportOrchestratorJob : IJob
    {
        private readonly IQueueClient _queueClient;

        public CosmosExportOrchestratorJob(IQueueClient queueClient)
        {
            _queueClient = EnsureArg.IsNotNull(queueClient, nameof(queueClient));
        }

        public async Task<string> ExecuteAsync(JobInfo jobInfo, IProgress<string> progress, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(jobInfo, nameof(jobInfo));
            EnsureArg.IsNotNull(progress, nameof(progress));

            var record = JsonConvert.DeserializeObject<ExportJobRecord>(jobInfo.Definition);
            record.QueuedTime = jobInfo.CreateDate; // get record of truth

            var groupJobs = await _queueClient.GetJobByGroupIdAsync((byte)QueueType.Export, jobInfo.GroupId, true, cancellationToken);

            if (groupJobs.Count == 1)
            {
                var processingRecord = CreateExportRecord(record, jobInfo.GroupId);
                await _queueClient.EnqueueAsync((byte)QueueType.Export, new[] { JsonConvert.SerializeObject(processingRecord) }, jobInfo.GroupId, false, false, cancellationToken);
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
