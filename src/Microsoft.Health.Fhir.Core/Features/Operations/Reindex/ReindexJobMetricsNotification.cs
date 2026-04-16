// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Metrics;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Reindex
{
    /// <summary>
    /// Represents the metrics notification payload for a completed reindex job.
    /// </summary>
    public class ReindexJobMetricsNotification : IMetricsNotification
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ReindexJobMetricsNotification"/> class.
        /// </summary>
        /// <param name="id">The reindex job ID.</param>
        /// <param name="status">The final reindex job status.</param>
        /// <param name="createTime">The time the reindex job was created.</param>
        /// <param name="endTime">The time the reindex job completed.</param>
        /// <param name="succeededCount">The number of successfully reindexed resources.</param>
        /// <param name="failedCount">The number of resources that failed reindexing.</param>
        /// <param name="createdChildJobs">The number of child processing jobs created.</param>
        /// <param name="completedChildJobs">The number of child processing jobs completed.</param>
        /// <param name="searchParams">The search parameter URIs included in reindexing.</param>
        /// <param name="resourceTypes">The resource types included in reindexing.</param>
        public ReindexJobMetricsNotification(
            string id,
            string status,
            DateTimeOffset createTime,
            DateTimeOffset endTime,
            long? succeededCount,
            long? failedCount,
            int createdChildJobs,
            int completedChildJobs,
            IReadOnlyCollection<string> searchParams,
            IReadOnlyCollection<string> resourceTypes)
        {
            FhirOperation = AuditEventSubType.Reindex;
            ResourceType = null;

            Id = id;
            Status = status;
            CreateTime = createTime;
            EndTime = endTime;
            SucceededCount = succeededCount;
            FailedCount = failedCount;
            CreatedChildJobs = createdChildJobs;
            CompletedChildJobs = completedChildJobs;
            SearchParams = searchParams;
            ResourceTypes = resourceTypes;
        }

        /// <summary>
        /// Gets the FHIR operation name associated with this metrics notification.
        /// </summary>
        public string FhirOperation { get; }

        /// <summary>
        /// Gets the resource type for this metrics notification.
        /// </summary>
        public string ResourceType { get; }

        /// <summary>
        /// Gets the reindex job ID.
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// Gets the final reindex job status.
        /// </summary>
        public string Status { get; }

        /// <summary>
        /// Gets the time the reindex job was created.
        /// </summary>
        public DateTimeOffset CreateTime { get; }

        /// <summary>
        /// Gets the time the reindex job completed.
        /// </summary>
        public DateTimeOffset EndTime { get; }

        /// <summary>
        /// Gets the number of successfully reindexed resources.
        /// </summary>
        public long? SucceededCount { get; }

        /// <summary>
        /// Gets the number of resources that failed reindexing.
        /// </summary>
        public long? FailedCount { get; }

        /// <summary>
        /// Gets the number of child processing jobs created.
        /// </summary>
        public int CreatedChildJobs { get; }

        /// <summary>
        /// Gets the number of child processing jobs completed.
        /// </summary>
        public int CompletedChildJobs { get; }

        /// <summary>
        /// Gets the search parameter URIs included in reindexing.
        /// </summary>
        public IReadOnlyCollection<string> SearchParams { get; }

        /// <summary>
        /// Gets the resource types included in reindexing.
        /// </summary>
        public IReadOnlyCollection<string> ResourceTypes { get; }
    }
}
