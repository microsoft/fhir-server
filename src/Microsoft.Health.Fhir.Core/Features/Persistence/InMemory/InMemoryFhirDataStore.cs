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
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Exceptions.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.Features.Persistence.InMemory
{
    public class InMemoryFhirDataStore : IFhirDataStore
    {
        private static readonly Dictionary<string, List<ResourceWrapper>> List = new Dictionary<string, List<ResourceWrapper>>();
        private static readonly Dictionary<string, ExportJobRecord> ExportJobData = new Dictionary<string, ExportJobRecord>();

        public Task<UpsertOutcome> UpsertAsync(
            ResourceWrapper resource,
            WeakETag weakETag,
            bool allowCreate,
            bool keepHistory,
            CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));

            string lookupKey = GetKey(resource.ResourceTypeName, resource.ResourceId);

            var outcome = SaveOutcomeType.Updated;

            if (!List.ContainsKey(lookupKey) && !allowCreate && weakETag == null)
            {
                throw new MethodNotAllowedException(Core.Resources.ResourceCreationNotAllowed);
            }
            else if (!List.ContainsKey(lookupKey) && weakETag == null)
            {
                outcome = SaveOutcomeType.Created;
                List[lookupKey] = new List<ResourceWrapper>();
            }
            else if (List.ContainsKey(lookupKey) && weakETag != null
                && List[lookupKey].OrderByDescending(x => x.LastModified).First().Version != weakETag.VersionId)
            {
                throw new ResourceConflictException(weakETag);
            }
            else if (!List.ContainsKey(lookupKey) && weakETag != null)
            {
                throw new ResourceConflictException(weakETag);
            }

            if (!keepHistory && List[lookupKey].Any())
            {
                List[lookupKey].RemoveAt(List[lookupKey].Count - 1);
            }

            var upsertedVersion = new ResourceWrapper(
                resource.ResourceId,
                Guid.NewGuid().ToString(),
                resource.ResourceTypeName,
                resource.RawResource,
                resource.Request,
                Clock.UtcNow,
                resource.IsDeleted,
                resource.SearchIndices,
                resource.CompartmentIndices,
                resource.LastModifiedClaims);

            List[lookupKey].Add(upsertedVersion);

            return Task.FromResult(new UpsertOutcome(upsertedVersion, outcome));
        }

        public Task<ResourceWrapper> GetAsync(ResourceKey key, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(key, nameof(key));

            string lookupKey = GetKey(key.ResourceType, key.Id);

            if (!List.ContainsKey(lookupKey))
            {
                return Task.FromResult((ResourceWrapper)null);
            }

            if (!string.IsNullOrEmpty(key.VersionId))
            {
                return Task.FromResult(List[lookupKey].SingleOrDefault(x => x.ResourceId == key.Id && x.Version == key.VersionId));
            }

            return Task.FromResult(List[lookupKey].LastOrDefault());
        }

        public Task HardDeleteAsync(ResourceKey key, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(key, nameof(key));

            string lookupKey = GetKey(key.ResourceType, key.Id);

            List.Remove(lookupKey);

            return Task.CompletedTask;
        }

        private static string GetKey(string resourceType, string resourceId)
        {
            return $"{resourceType}_{resourceId}";
        }

        public Task<ExportJobOutcome> CreateExportJobAsync(ExportJobRecord jobRecord, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(jobRecord);

            ExportJobData.Add(jobRecord.Id, jobRecord);
            return Task.FromResult(new ExportJobOutcome(jobRecord, WeakETag.FromVersionId("eTag")));
        }

        public Task<ExportJobOutcome> GetExportJobAsync(string jobId, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNullOrEmpty(jobId);

            if (!ExportJobData.ContainsKey(jobId))
            {
                throw new JobNotFoundException(string.Format(Core.Resources.JobNotFound, jobId));
            }

            ExportJobRecord jobRecord = ExportJobData[jobId];
            return Task.FromResult(new ExportJobOutcome(jobRecord, WeakETag.FromVersionId("eTag")));
        }

        public Task<ExportJobOutcome> ReplaceExportJobAsync(ExportJobRecord jobRecord, WeakETag eTag, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(jobRecord);

            if (!ExportJobData.ContainsKey(jobRecord.Id))
            {
                throw new JobNotFoundException(string.Format(Core.Resources.JobNotFound, jobRecord.Id));
            }

            ExportJobData[jobRecord.Id] = jobRecord;
            return Task.FromResult(new ExportJobOutcome(jobRecord, WeakETag.FromVersionId("eTag")));
        }
    }
}
