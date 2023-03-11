// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Messages.Delete;
using Microsoft.Health.JobManagement;

namespace Microsoft.Health.Fhir.Core.Features.Resources.Delete.Async;

public class CancelConditionalDeleteResourceAsyncHandler : IRequestHandler<DeleteConditionalDeleteResourceAsyncRequest, DeleteConditionalDeleteResourceAsyncResponse>
{
    private readonly IQueueClient _queueClient;

    public CancelConditionalDeleteResourceAsyncHandler(IQueueClient queueClient)
    {
        _queueClient = EnsureArg.IsNotNull(queueClient, nameof(queueClient));
    }

    public async Task<DeleteConditionalDeleteResourceAsyncResponse> Handle(DeleteConditionalDeleteResourceAsyncRequest request, CancellationToken cancellationToken)
    {
        EnsureArg.IsNotNull(request, nameof(request));

        IReadOnlyList<JobInfo> group = await _queueClient.GetJobByGroupIdAsync((byte)QueueType.ConditionalDelete, request.GroupId, false, cancellationToken);

        bool isAlreadyCancelled = group.Any(x => x.Status == JobStatus.Cancelled);
        if (isAlreadyCancelled)
        {
            throw new ResourceNotFoundException(string.Format(Core.Resources.JobNotFound, request.GroupId));
        }

        bool isAlreadyTerminal = group.Any(x => x.Status != JobStatus.Created && x.Status != JobStatus.Running);
        if (isAlreadyTerminal)
        {
            throw new BadRequestException(Core.Resources.OperationAlreadyCompleted);
        }

        await _queueClient.CancelJobByGroupIdAsync((int)QueueType.ConditionalDelete, request.GroupId, cancellationToken);

        return new DeleteConditionalDeleteResourceAsyncResponse();
    }
}
