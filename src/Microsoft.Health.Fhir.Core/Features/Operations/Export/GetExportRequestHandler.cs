// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Medino;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Core.Features.Operations.Security;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Messages.Export;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Export
{
    public class GetExportRequestHandler : IRequestHandler<GetExportRequest, GetExportResponse>
    {
        private readonly IFhirOperationDataStore _fhirOperationDataStore;
        private readonly IAuthorizationService<DataActions> _authorizationService;
        private readonly IExportSmartScopeAuthorizer _exportSmartScopeAuthorizer;

        public GetExportRequestHandler(
            IFhirOperationDataStore fhirOperationDataStore,
            IAuthorizationService<DataActions> authorizationService,
            IExportSmartScopeAuthorizer exportSmartScopeAuthorizer)
        {
            EnsureArg.IsNotNull(fhirOperationDataStore, nameof(fhirOperationDataStore));
            EnsureArg.IsNotNull(authorizationService, nameof(authorizationService));
            EnsureArg.IsNotNull(exportSmartScopeAuthorizer, nameof(exportSmartScopeAuthorizer));

            _fhirOperationDataStore = fhirOperationDataStore;
            _authorizationService = authorizationService;
            _exportSmartScopeAuthorizer = exportSmartScopeAuthorizer;
        }

        public async Task<GetExportResponse> HandleAsync(GetExportRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            await _authorizationService.CheckAccess(DataActions.Export, true, cancellationToken);

            ExportJobOutcome outcome = await _fhirOperationDataStore.GetExportJobByIdAsync(request.JobId, cancellationToken);

            // The authorizer applies SMART scope checks when applicable; otherwise this is a no-op.
            try
            {
                _exportSmartScopeAuthorizer.AuthorizeJobAccess(outcome.JobRecord);
            }
            catch (UnauthorizedFhirActionException)
            {
                // Do not reveal whether the job exists to a caller lacking SMART access.
                throw new JobNotFoundException(string.Format(Core.Resources.JobNotFound, request.JobId));
            }

            // We have an existing job. We will determine the response based on the status of the export operation.
            GetExportResponse exportResponse;

            if (outcome.JobRecord.Status == OperationStatus.Completed || outcome.JobRecord.Status == OperationStatus.Canceled)
            {
                List<ExportFileInfo> allFiles = new List<ExportFileInfo>();
                foreach (List<ExportFileInfo> fileList in outcome.JobRecord.Output.Values)
                {
                    allFiles.AddRange(fileList);
                }

                var jobResult = new ExportJobResult(
                    outcome.JobRecord.QueuedTime,
                    outcome.JobRecord.RequestUri,
                    requiresAccessToken: false,
                    allFiles.Select(x => x.ToExportOutputResponse()).OrderBy(x => x.Type, StringComparer.Ordinal).ToList(),
                    outcome.JobRecord.Error.Select(x => x.ToExportOutputResponse()).ToList(),
                    outcome.JobRecord.Issues);

                exportResponse = new GetExportResponse(HttpStatusCode.OK, jobResult);
            }
            else if (outcome.JobRecord.Status == OperationStatus.Failed || outcome.JobRecord.Status == OperationStatus.Canceled)
            {
                string failureReason = outcome.JobRecord.FailureDetails != null ? outcome.JobRecord.FailureDetails.FailureReason : Core.Resources.UnknownError;
                HttpStatusCode failureStatusCode = outcome.JobRecord.FailureDetails != null ? outcome.JobRecord.FailureDetails.FailureStatusCode : HttpStatusCode.InternalServerError;

                throw new OperationFailedException(
                    string.Format(Core.Resources.OperationFailed, OperationsConstants.Export, failureReason), failureStatusCode);
            }
            else
            {
                exportResponse = new GetExportResponse(HttpStatusCode.Accepted);
            }

            return exportResponse;
        }
    }
}
