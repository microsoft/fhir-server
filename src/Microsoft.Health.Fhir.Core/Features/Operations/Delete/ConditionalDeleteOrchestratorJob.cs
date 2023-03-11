// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Health.JobManagement;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Delete;

[JobTypeId((int)JobType.ConditionalDeleteOrchestrator)]
public class ConditionalDeleteOrchestratorJob : IJob
{
    private const string CompleteStatus = "Complete";
    private readonly IQueueClient _queueClient;

    public ConditionalDeleteOrchestratorJob(IQueueClient queueClient)
    {
        _queueClient = EnsureArg.IsNotNull(queueClient, nameof(queueClient));
    }

    public async Task<string> ExecuteAsync(JobInfo jobInfo, IProgress<string> progress, CancellationToken cancellationToken)
    {
        ConditionalDeleteJobInfo data = jobInfo.DeserializeDefinition<ConditionalDeleteJobInfo>();

        // TODO: Add logic to split the query into multiple queries and enqueue multiple jobs

        var processingJob = new ConditionalDeleteJobInfo(
            (int)JobType.ConditionalDeleteProcessing,
            data.ResourceType,
            data.ConditionalParameters,
            data.DeleteOperation,
            data.Principal,
            data.ActivityId,
            data.RequestUri,
            data.RequestUri);

        await _queueClient.EnqueueAsync(QueueType.ConditionalDelete, groupId: jobInfo.Id, cancellationToken: cancellationToken, definitions: processingJob);

        return CompleteStatus;
    }
}
