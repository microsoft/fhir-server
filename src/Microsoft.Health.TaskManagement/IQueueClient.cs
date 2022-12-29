// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Health.JobManagement
{
    public interface IQueueClient
    {
        /// <summary>
        /// Ensure the client initialized.
        /// </summary>
        bool IsInitialized();

        /// <summary>
        /// Enqueue new jobs
        /// </summary>
        /// <param name="queueType">Queue Type for new jobs</param>
        /// <param name="definitions">Job definiation</param>
        /// <param name="groupId">Group id for jobs. Optional</param>
        /// <param name="forceOneActiveJobGroup">Only enqueue job only if there's no active job with same queue type.</param>
        /// <param name="isCompleted">Enqueue completed jobs.</param>
        /// <param name="cancellationToken">Cancellation Token</param>
        /// <returns>Job ids for all jobs, include existed jobs.</returns>
        public Task<IReadOnlyList<JobInfo>> EnqueueAsync(byte queueType, string[] definitions, long? groupId, bool forceOneActiveJobGroup, bool isCompleted, CancellationToken cancellationToken);

        /// <summary>
        /// Dequeue multiple jobs
        /// </summary>
        /// <param name="queueType">Queue Type</param>
        /// <param name="numberOfJobsToDequeue">Number of jobs to dequeue</param>
        /// <param name="worker">Current worker name</param>
        /// <param name="heartbeatTimeoutSec">Heartbeat timeout for retry</param>
        /// <param name="cancellationToken">Cancellation Token</param>
        public Task<IReadOnlyCollection<JobInfo>> DequeueJobsAsync(byte queueType, int numberOfJobsToDequeue, string worker, int heartbeatTimeoutSec, CancellationToken cancellationToken);

        /// <summary>
        /// Dequeue job
        /// </summary>
        /// <param name="queueType">Queue Type</param>
        /// <param name="worker">Current worker name</param>
        /// <param name="heartbeatTimeoutSec">Heartbeat timeout for retry</param>
        /// <param name="cancellationToken">Cancellation Token</param>
        /// <param name="jobId">Requested job id for dequeue</param>
        public Task<JobInfo> DequeueAsync(byte queueType, string worker, int heartbeatTimeoutSec, CancellationToken cancellationToken, long? jobId = null);

        /// <summary>
        /// Get job by id
        /// </summary>
        /// <param name="queueType">Queue Type</param>
        /// <param name="jobId">Job id</param>
        /// <param name="returnDefinition">Return definition</param>
        /// <param name="cancellationToken">Cancellation token</param>
        public Task<JobInfo> GetJobByIdAsync(byte queueType, long jobId, bool returnDefinition, CancellationToken cancellationToken);

        /// <summary>
        /// Get job by ids
        /// </summary>
        /// <param name="queueType">Queue Type</param>
        /// <param name="jobIds">Job ids list</param>
        /// <param name="returnDefinition">Return definition</param>
        /// <param name="cancellationToken">Cancellation token</param>
        public Task<IReadOnlyList<JobInfo>> GetJobsByIdsAsync(byte queueType, long[] jobIds, bool returnDefinition, CancellationToken cancellationToken);

        /// <summary>
        /// Get jobs by group id
        /// </summary>
        /// <param name="queueType">Queue Type</param>
        /// <param name="groupId">Job group id</param>
        /// <param name="returnDefinition">Return definition</param>
        /// <param name="cancellationToken">Cancellation token</param>
        public Task<IReadOnlyList<JobInfo>> GetJobByGroupIdAsync(byte queueType, long groupId, bool returnDefinition, CancellationToken cancellationToken);

        /// <summary>
        /// Cancel jobs by group id
        /// </summary>
        /// <param name="queueType">Queue Type</param>
        /// <param name="groupId">Job group id</param>
        /// <param name="cancellationToken">Cancellation Token</param>
        public Task CancelJobByGroupIdAsync(byte queueType, long groupId, CancellationToken cancellationToken);

        /// <summary>
        /// Cancel jobs by id
        /// </summary>
        /// <param name="queueType">Queue Type</param>
        /// <param name="jobId">Job id</param>
        /// <param name="cancellationToken">Cancellation Token</param>
        public Task CancelJobByIdAsync(byte queueType, long jobId, CancellationToken cancellationToken);

        /// <summary>
        /// Complete job
        /// </summary>
        /// <param name="jobInfo">Job info for complete</param>
        /// <param name="requestCancellationOnFailure">Cancel other jobs with same group id if this job failed.</param>
        /// <param name="cancellationToken">Cancellation token</param>
        public Task CompleteJobAsync(JobInfo jobInfo, bool requestCancellationOnFailure, CancellationToken cancellationToken);

        /// <summary>
        /// Executes job with heartbeats. Put heartbeat logic saves Data and Result from provided jobInfo.
        /// Usage of this method should be discouraged.
        /// </summary>
        /// <param name="jobInfo">job info</param>
        /// <param name="action">job action</param>
        /// <param name="heartbeatPeriod">heartbeat period in seconds</param>
        /// <param name="cancellationTokenSource">cancellationTokenSource</param>
        /// <returns>result</returns>
        public Task<string> ExecuteJobWithHeartbeats(JobInfo jobInfo, Func<CancellationTokenSource, Task<string>> action, TimeSpan heartbeatPeriod, CancellationTokenSource cancellationTokenSource);

        /// <summary>
        /// Executes job with light weight heartbeats.
        /// </summary>
        /// <param name="queueType">queue type</param>
        /// <param name="jobId">job id</param>
        /// <param name="version">job version</param>
        /// <param name="action">job action</param>
        /// <param name="heartbeatPeriod">heartbeat period</param>
        /// <param name="cancellationTokenSource">cancellationTokenSource</param>
        /// <returns>result</returns>
        public Task<string> ExecuteJobWithHeartbeats(byte queueType, long jobId, long version, Func<CancellationTokenSource, Task<string>> action, TimeSpan heartbeatPeriod, CancellationTokenSource cancellationTokenSource);
    }
}
