// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Delete;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Messages.Delete;
using Microsoft.Health.JobManagement;
using Microsoft.IdentityModel.Tokens;

namespace Microsoft.Health.Fhir.Core.Features.Resources.Delete.Async;

public class ConditionalDeleteResourceAsyncHandler : IRequestHandler<ConditionalDeleteResourceAsyncRequest, ConditionalDeleteResourceAsyncResponse>
{
    private readonly RequestContextAccessor<IFhirRequestContext> _requestContextAccessor;
    private readonly IQueueClient _queueClient;
    private readonly IAuthorizationService<DataActions> _authorizationService;

    public ConditionalDeleteResourceAsyncHandler(
        IQueueClient queueClient,
        IAuthorizationService<DataActions> authorizationService,
        RequestContextAccessor<IFhirRequestContext> requestContextAccessor)
    {
        _requestContextAccessor = EnsureArg.IsNotNull(requestContextAccessor, nameof(requestContextAccessor));
        _queueClient = EnsureArg.IsNotNull(queueClient, nameof(queueClient));
        _authorizationService = EnsureArg.IsNotNull(authorizationService, nameof(authorizationService));
    }

    public async Task<ConditionalDeleteResourceAsyncResponse> Handle(ConditionalDeleteResourceAsyncRequest request, CancellationToken cancellationToken)
    {
        EnsureArg.IsNotNull(request, nameof(request));

        DataActions dataActions = (request.DeleteOperation == DeleteOperation.SoftDelete ? DataActions.Delete : DataActions.HardDelete | DataActions.Delete) | DataActions.Read;

        if (await _authorizationService.CheckAccess(dataActions, cancellationToken) != dataActions)
        {
            throw new UnauthorizedFhirActionException();
        }

        var jobInfo = new ConditionalDeleteJobInfo(
            (byte)JobType.ConditionalDeleteOrchestrator,
            request.ResourceType,
            request.ConditionalParameters.ToList(),
            request.DeleteOperation,
            _requestContextAccessor.RequestContext.Principal.ToBase64(),
            _requestContextAccessor.RequestContext.CorrelationId ?? Activity.Current?.Id,
            _requestContextAccessor.RequestContext.Uri,
            _requestContextAccessor.RequestContext.BaseUri);

        IReadOnlyList<JobInfo> jobId = await _queueClient.EnqueueAsync(QueueType.ConditionalDelete, cancellationToken: cancellationToken, definitions: jobInfo);

        return new ConditionalDeleteResourceAsyncResponse(jobId[0].Id.ToString());
    }
}
