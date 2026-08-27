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
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.JobManagement;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Export
{
    public abstract class ExportOrchestratorJob : IJob
    {
        public abstract Task<string> ExecuteAsync(JobInfo jobInfo, CancellationToken cancellationToken);

        protected static ExportJobRecord ExtractJobRecord(JobInfo jobInfo)
        {
            var record = jobInfo.DeserializeDefinition<ExportJobRecord>();
            record.QueuedTime = jobInfo.CreateDate; // get record of truth

            // If Till was not explicitly set by the user, use the job's CreateDate
            if (!record.IsTillExplicit)
            {
                record.Till = new PartialDateTime(new DateTimeOffset(jobInfo.CreateDate, TimeSpan.Zero));
            }

            return record;
        }

        protected static ExportJobRecord CreateExportRecord(ExportJobRecord record, long groupId, string resourceType = null, PartialDateTime since = null, PartialDateTime till = null, string startSurrogateId = null, string endSurrogateId = null, string globalStartSurrogateId = null, string globalEndSurrogateId = null, string feedRange = null)
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
                        since: since == null ? record.Since : since,
                        till: till == null ? record.Till : till,
                        startSurrogateId: startSurrogateId,
                        endSurrogateId: endSurrogateId,
                        globalStartSurrogateId: globalStartSurrogateId,
                        globalEndSurrogateId: globalEndSurrogateId,
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
                        includeHistory: record.IncludeHistory,
                        includeDeleted: record.IncludeDeleted,
                        schemaVersion: record.SchemaVersion,
                        typeId: (int)JobType.ExportProcessing,
                        smartRequest: record.SmartRequest);
            rec.Id = string.Empty;
            rec.QueuedTime = record.QueuedTime; // preserve create date of coordinator job in form of queued time for all children, so same time is used on file names.

            return rec;
        }

        protected static void ValidateResourceTypes(ISearchService searchService, IEnumerable<string> resourceTypes, ExportJobRecord record)
        {
            EnsureArg.IsNotNull(searchService, nameof(searchService));
            EnsureArg.IsNotNull(record, nameof(record));

            var invalidTypes = resourceTypes?.Where(t => !searchService.IsValidResourceType(t)).ToList();
            if (invalidTypes != null && invalidTypes.Count > 0)
            {
                var message = $"Invalid resource type(s): {string.Join(", ", invalidTypes)}";
                record.FailureDetails = new JobFailureDetails(message, HttpStatusCode.BadRequest);
                throw new JobExecutionException(message, record, false);
            }
        }
    }
}
