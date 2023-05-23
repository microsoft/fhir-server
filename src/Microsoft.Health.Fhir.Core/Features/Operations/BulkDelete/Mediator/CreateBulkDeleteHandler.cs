// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Health.Core;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Messages.Delete;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.JobManagement;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Operations.BulkDelete.Mediator
{
    public class CreateBulkDeleteHandler : IRequestHandler<CreateBulkDeleteRequest, CreateBulkDeleteResponse>
    {
        private IAuthorizationService<DataActions> _authorizationService;
        private IQueueClient _queueClient;
        private RequestContextAccessor<IFhirRequestContext> _contextAccessor;

        public CreateBulkDeleteHandler(
            IAuthorizationService<DataActions> authorizationService,
            IQueueClient queueClient,
            RequestContextAccessor<IFhirRequestContext> contextAccessor)
        {
            _authorizationService = authorizationService;
            _queueClient = queueClient;
            _contextAccessor = contextAccessor;
        }

        public async Task<CreateBulkDeleteResponse> Handle(CreateBulkDeleteRequest request, CancellationToken cancellationToken)
        {
            DataActions requiredDataAction = request.DeleteOperation == DeleteOperation.SoftDelete ? DataActions.Delete : DataActions.HardDelete | DataActions.Delete;
            if (await _authorizationService.CheckAccess(requiredDataAction, cancellationToken) != requiredDataAction)
            {
                throw new UnauthorizedFhirActionException();
            }

            var searchParameters = new List<Tuple<string, string>>(request.ConditionalParameters); // remove read only restriction
            var dateCurrent = new PartialDateTime(Clock.UtcNow);
            searchParameters.Add(Tuple.Create("_lastUpdated", $"lt{dateCurrent}"));

            var definitions = new List<string>();
            var processingDefinition = new BulkDeleteDefinition(JobType.BulkDeleteOrchestrator, request.DeleteOperation, request.ResourceType, searchParameters, _contextAccessor.RequestContext.Uri.ToString(), _contextAccessor.RequestContext.BaseUri.ToString());
            definitions.Add(JsonConvert.SerializeObject(processingDefinition));
            var jobInfo = await _queueClient.EnqueueAsync((byte)QueueType.BulkDelete, definitions.ToArray(), null, false, false, cancellationToken);

            // Bad, fix this to be better
            if (jobInfo.Count < 1)
            {
                throw new JobNotExistException("Failed to create job");
            }

            return new CreateBulkDeleteResponse(jobInfo[0].Id);
        }
    }
}
