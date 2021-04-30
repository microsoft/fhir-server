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
using Microsoft.Health.Core.Features.Security;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Messages.Import;
using Microsoft.Health.TaskManagement;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    /// <summary>
    /// MediatR request handler. Called when the BulkImportController creates an BulkImport job.
    /// </summary>
    public class CreateBulkImportRequestHandler : IRequestHandler<CreateImportRequest, CreateImportResponse>
    {
        private readonly IClaimsExtractor _claimsExtractor;
        private readonly ITaskManager _taskManager;
        private readonly ILogger<CreateBulkImportRequestHandler> _logger;
        private readonly IAuthorizationService<DataActions> _authorizationService;

        public CreateBulkImportRequestHandler(
            IClaimsExtractor claimsExtractor,
            ITaskManager taskManager,
            ILogger<CreateBulkImportRequestHandler> logger,
            IAuthorizationService<DataActions> authorizationService)
        {
            EnsureArg.IsNotNull(claimsExtractor, nameof(claimsExtractor));
            EnsureArg.IsNotNull(taskManager, nameof(taskManager));
            EnsureArg.IsNotNull(authorizationService, nameof(authorizationService));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _claimsExtractor = claimsExtractor;
            _taskManager = taskManager;
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

            throw new NotImplementedException();
        }
    }
}
