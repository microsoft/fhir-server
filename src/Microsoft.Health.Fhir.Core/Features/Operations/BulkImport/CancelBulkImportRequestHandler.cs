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
using Microsoft.Health.Fhir.Core.Messages.BulkImport;
using Microsoft.Health.Fhir.TaskManagement;

namespace Microsoft.Health.Fhir.Core.Features.Operations.BulkImport
{
    public class CancelBulkImportRequestHandler : IRequestHandler<CancelBulkImportRequest, CancelBulkImportResponse>
    {
        private readonly ITaskManager _taskManager;
        private readonly IAuthorizationService<DataActions> _authorizationService;
        private readonly ILogger<CancelBulkImportRequestHandler> _logger;

        public CancelBulkImportRequestHandler(ITaskManager taskManager, IAuthorizationService<DataActions> authorizationService, ILogger<CancelBulkImportRequestHandler> logger)
        {
            EnsureArg.IsNotNull(taskManager, nameof(taskManager));
            EnsureArg.IsNotNull(authorizationService, nameof(authorizationService));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _taskManager = taskManager;
            _authorizationService = authorizationService;
            _logger = logger;
        }

        public async Task<CancelBulkImportResponse> Handle(CancelBulkImportRequest request, CancellationToken cancellationToken)
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
