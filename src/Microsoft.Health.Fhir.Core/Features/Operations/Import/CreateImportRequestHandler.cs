// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Messages.Import;
using Microsoft.Health.JobManagement;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    /// <summary>
    /// MediatR request handler. Called when the ImportController creates an Import job.
    /// </summary>
    public class CreateImportRequestHandler : IRequestHandler<CreateImportRequest, CreateImportResponse>
    {
        private readonly IQueueClient _queueClient;
        private readonly ILogger<CreateImportRequestHandler> _logger;
        private readonly IAuthorizationService<DataActions> _authorizationService;

        public CreateImportRequestHandler(
            IQueueClient queueClient,
            ILogger<CreateImportRequestHandler> logger,
            IAuthorizationService<DataActions> authorizationService)
        {
            EnsureArg.IsNotNull(queueClient, nameof(queueClient));
            EnsureArg.IsNotNull(authorizationService, nameof(authorizationService));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _queueClient = queueClient;
            _authorizationService = authorizationService;
            _logger = logger;
        }

        public async Task<CreateImportResponse> Handle(CreateImportRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            if (await _authorizationService.CheckAccess(DataActions.Import, cancellationToken) != DataActions.Import)
            {
                throw new UnauthorizedFhirActionException();
            }

            var definitionObj = new ImportOrchestratorJobDefinition()
            {
                TypeId = (int)JobType.ImportOrchestrator,
                RequestUri = request.RequestUri,
                BaseUri = new Uri(request.RequestUri.GetLeftPart(UriPartial.Authority)),
                Input = request.Input,
                InputFormat = request.InputFormat,
                InputSource = request.InputSource,
                StorageDetail = request.StorageDetail,
                ImportMode = request.ImportMode,
            };

            var definition = JsonConvert.SerializeObject(definitionObj);
            var jobInfo = (await _queueClient.EnqueueAsync((byte)QueueType.Import, new string[] { definition }, null, false, false, cancellationToken))[0];
            return new CreateImportResponse(jobInfo.Id.ToString());
        }
    }
}
