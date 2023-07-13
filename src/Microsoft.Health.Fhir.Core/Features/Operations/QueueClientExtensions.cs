// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Health.JobManagement;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Operations;

public static class QueueClientExtensions
{
    public static Task<IReadOnlyList<JobInfo>> EnqueueAsync<T>(this IQueueClient queueClient, QueueType queueType, CancellationToken cancellationToken, long? groupId = null, bool forceOneActiveJobGroup = false, bool isCompleted = false, params T[] definitions)
     where T : IJobData
    {
        EnsureArg.HasItems(definitions, nameof(definitions));
        return queueClient.EnqueueAsync((byte)queueType, definitions.Select(x => JsonConvert.SerializeObject(x)).ToArray(), groupId, forceOneActiveJobGroup, isCompleted, cancellationToken);
    }

    public static Task<IReadOnlyCollection<JobInfo>> DequeueJobsAsync(this IQueueClient queueClient, QueueType queueType, int numberOfJobsToDequeue, string worker, int heartbeatTimeoutSec, CancellationToken cancellationToken)
    {
        return queueClient.DequeueJobsAsync((byte)queueType, numberOfJobsToDequeue, worker, heartbeatTimeoutSec, cancellationToken);
    }

    public static Task<JobInfo> DequeueAsync(this IQueueClient queueClient, QueueType queueType, string worker, int heartbeatTimeoutSec, CancellationToken cancellationToken, long? jobId = null)
    {
        return queueClient.DequeueAsync((byte)queueType, worker, heartbeatTimeoutSec, cancellationToken, jobId);
    }

    public static Task<JobInfo> GetJobByIdAsync(this IQueueClient queueClient, QueueType queueType, long jobId, bool returnDefinition, CancellationToken cancellationToken)
    {
        return queueClient.GetJobByIdAsync((byte)queueType, jobId, returnDefinition, cancellationToken);
    }

    public static Task<IReadOnlyList<JobInfo>> GetJobsByIdsAsync(this IQueueClient queueClient, QueueType queueType, long[] jobIds, bool returnDefinition, CancellationToken cancellationToken)
    {
        return queueClient.GetJobsByIdsAsync((byte)queueType, jobIds, returnDefinition, cancellationToken);
    }

    public static Task<IReadOnlyList<JobInfo>> GetJobByGroupIdAsync(this IQueueClient queueClient, QueueType queueType, long groupId, bool returnDefinition, CancellationToken cancellationToken)
    {
        return queueClient.GetJobByGroupIdAsync((byte)queueType, groupId, returnDefinition, cancellationToken);
    }

    public static Task CancelJobByGroupIdAsync(this IQueueClient queueClient, QueueType queueType, long groupId, CancellationToken cancellationToken)
    {
        return queueClient.CancelJobByGroupIdAsync((byte)queueType, groupId, cancellationToken);
    }

    public static Task CancelJobByIdAsync(this IQueueClient queueClient, QueueType queueType, long jobId, CancellationToken cancellationToken)
    {
        return queueClient.CancelJobByIdAsync((byte)queueType, jobId, cancellationToken);
    }
}
