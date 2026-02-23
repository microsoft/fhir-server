// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.JobManagement;

namespace Microsoft.Health.Fhir.Core.Features.Operations.GetJobStatus
{
    /// <summary>
    /// Represents the status information for an async job.
    /// </summary>
    public class JobStatusInfo
    {
        /// <summary>
        /// Gets or sets the group identifier for the job.
        /// </summary>
        public long GroupId { get; set; }

        /// <summary>
        /// Gets or sets the type of the job (e.g., Export, Import, Reindex).
        /// </summary>
        public string JobType { get; set; }

        /// <summary>
        /// Gets or sets the queue type.
        /// </summary>
        public QueueType QueueType { get; set; }

        /// <summary>
        /// Gets or sets the status of the job.
        /// </summary>
        public Microsoft.Health.JobManagement.JobStatus Status { get; set; }

        /// <summary>
        /// Gets or sets the content location URL for the job.
        /// </summary>
        public Uri ContentLocation { get; set; }

        /// <summary>
        /// Gets or sets the date and time when the job was created.
        /// </summary>
        public DateTime CreateDate { get; set; }

        /// <summary>
        /// Gets or sets the date and time when the job ended.
        /// </summary>
        public DateTime? EndDate { get; set; }
    }
}
