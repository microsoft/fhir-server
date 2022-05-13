// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Core;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Messages.Import;
using Microsoft.Health.TaskManagement;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    /// <summary>
    /// MediatR request handler. Called when the ImportController creates an Import job.
    /// </summary>
    public class CreateImportRequestHandler : IRequestHandler<CreateImportRequest, CreateImportResponse>
    {
        private readonly IQueueClient _queueClient;
        private readonly ISequenceIdGenerator<long> _sequenceIdGenerator;
        private readonly ImportTaskConfiguration _importTaskConfiguration;
        private readonly TaskHostingConfiguration _taskHostingConfiguration;
        private readonly ILogger<CreateImportRequestHandler> _logger;
        private readonly IAuthorizationService<DataActions> _authorizationService;

        public CreateImportRequestHandler(
            IQueueClient queueClient,
            ISequenceIdGenerator<long> sequenceIdGenerator,
            IOptions<OperationsConfiguration> operationsConfig,
            IOptions<TaskHostingConfiguration> taskHostingConfiguration,
            ILogger<CreateImportRequestHandler> logger,
            IAuthorizationService<DataActions> authorizationService)
        {
            EnsureArg.IsNotNull(queueClient, nameof(queueClient));
            EnsureArg.IsNotNull(sequenceIdGenerator, nameof(sequenceIdGenerator));
            EnsureArg.IsNotNull(operationsConfig.Value, nameof(operationsConfig));
            EnsureArg.IsNotNull(taskHostingConfiguration.Value, nameof(taskHostingConfiguration));
            EnsureArg.IsNotNull(authorizationService, nameof(authorizationService));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _queueClient = queueClient;
            _sequenceIdGenerator = sequenceIdGenerator;
            _importTaskConfiguration = operationsConfig.Value.Import;
            _taskHostingConfiguration = taskHostingConfiguration.Value;
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

            ImportOrchestratorTaskInputData inputData = new ImportOrchestratorTaskInputData()
            {
                TypeId = ImportOrchestratorTask.ImportOrchestratorTaskId,
                RequestUri = request.RequestUri,
                BaseUri = new Uri(request.RequestUri.GetLeftPart(UriPartial.Authority)),
                Input = request.Input,
                InputFormat = request.InputFormat,
                InputSource = request.InputSource,
                StorageDetail = request.StorageDetail,
                TaskCreateTime = Clock.UtcNow,
                StartSequenceId = _sequenceIdGenerator.GetCurrentSequenceId(),
            };

            string definition = JsonConvert.SerializeObject(inputData);

            try
            {
                TaskInfo taskInfo = (await _queueClient.EnqueueAsync(ImportConstants.ImportQueueType, new string[] { definition }, null, true, false, cancellationToken)).First();
                return new CreateImportResponse(taskInfo.Id.ToString());
            }
            catch (TaskConflictException)
            {
                _logger.LogInformation("Already a running import task.");
                throw new OperationFailedException(Core.Resources.ImportTaskIsRunning, HttpStatusCode.Conflict);
            }
        }
    }
}
