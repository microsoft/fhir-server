// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Messages.Import;
using Microsoft.Health.JobManagement;
using Newtonsoft.Json;
using JobStatus = Microsoft.Health.JobManagement.JobStatus;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    public class GetImportRequestHandler : IRequestHandler<GetImportRequest, GetImportResponse>
    {
        private readonly IFhirOperationDataStore _fhirOperationDataStore;
        private readonly IAuthorizationService<DataActions> _authorizationService;

        public GetImportRequestHandler(IFhirOperationDataStore fhirOperationDataStore, IAuthorizationService<DataActions> authorizationService)
        {
            EnsureArg.IsNotNull(fhirOperationDataStore, nameof(fhirOperationDataStore));
            EnsureArg.IsNotNull(authorizationService, nameof(authorizationService));

            _fhirOperationDataStore = fhirOperationDataStore;
            _authorizationService = authorizationService;
        }

        public async Task<GetImportResponse> Handle(GetImportRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            if (await _authorizationService.CheckAccess(DataActions.Import, cancellationToken) != DataActions.Import)
            {
                throw new UnauthorizedFhirActionException();
            }

            var outcome = await _fhirOperationDataStore.GetImportJobByIdAsync(request.JobId.ToString(), cancellationToken);

            // If orchestrator is still waiting to be processed or queuing child jobs, return accepted with no body.
            if (outcome.OrchetratorJob.Status == JobStatus.Created || outcome.OrchetratorJob.Status == JobStatus.Running)
            {
                return new GetImportResponse(HttpStatusCode.Accepted);
            }

            if (outcome.Status == JobStatus.Cancelled)
            {
                throw new OperationFailedException(Core.Resources.UserRequestedCancellation, HttpStatusCode.BadRequest);
            }

            if (outcome.OrchetratorJob.Status != JobStatus.Completed)
            {
                throw new OperationFailedException(Core.Resources.UnknownError, HttpStatusCode.InternalServerError);
            }

            var coordResult = JsonConvert.DeserializeObject<ImportOrchestratorJobResult>(outcome.OrchetratorJob.Result);
            var results = GetProcessingResultAsync(outcome.ProcessingJobs);

            var result = new ImportJobResult() { Request = coordResult.Request, TransactionTime = outcome.OrchetratorJob.CreateDate, Output = results.Completed, Error = results.Failed };
            return new GetImportResponse(outcome.Status == JobStatus.Completed ? HttpStatusCode.OK : HttpStatusCode.Accepted, result);

            static (List<ImportOperationOutcome> Completed, List<ImportFailedOperationOutcome> Failed) GetProcessingResultAsync(IList<JobInfo> jobs)
            {
                var completed = new List<ImportOperationOutcome>();
                var failed = new List<ImportFailedOperationOutcome>();
                foreach (var job in jobs.Where(_ => _.Status == JobStatus.Completed))
                {
                    var definition = JsonConvert.DeserializeObject<ImportProcessingJobDefinition>(job.Definition);
                    var result = JsonConvert.DeserializeObject<ImportProcessingJobResult>(job.Result);
                    completed.Add(new ImportOperationOutcome() { Type = definition.ResourceType, Count = result.SucceededResources, InputUrl = new Uri(definition.ResourceLocation) });
                    if (result.FailedResources > 0)
                    {
                        failed.Add(new ImportFailedOperationOutcome() { Type = definition.ResourceType, Count = result.FailedResources, InputUrl = new Uri(definition.ResourceLocation), Url = result.ErrorLogLocation });
                    }
                }

                // group success results by url
                var groupped = completed.GroupBy(o => o.InputUrl).Select(g => new ImportOperationOutcome() { Type = g.First().Type, Count = g.Sum(_ => _.Count), InputUrl = g.Key }).ToList();

                return (groupped, failed);
            }
        }
    }
}
