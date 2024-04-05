// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        private readonly IQueueClient _queueClient;
        private readonly IAuthorizationService<DataActions> _authorizationService;

        public GetImportRequestHandler(IQueueClient queueClient, IAuthorizationService<DataActions> authorizationService)
        {
            EnsureArg.IsNotNull(queueClient, nameof(queueClient));
            EnsureArg.IsNotNull(authorizationService, nameof(authorizationService));

            _queueClient = queueClient;
            _authorizationService = authorizationService;
        }

        public async Task<GetImportResponse> Handle(GetImportRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            if (await _authorizationService.CheckAccess(DataActions.Import, cancellationToken) != DataActions.Import)
            {
                throw new UnauthorizedFhirActionException();
            }

            var coordInfo = await _queueClient.GetJobByIdAsync(QueueType.Import, request.JobId, false, cancellationToken);
            if (coordInfo == null || coordInfo.Status == JobStatus.Archived)
            {
                throw new ResourceNotFoundException(string.Format(Core.Resources.ImportJobNotFound, request.JobId));
            }
            else if (coordInfo.Status == JobStatus.Created || coordInfo.Status == JobStatus.Running)
            {
                return new GetImportResponse(HttpStatusCode.Accepted);
            }
            else if (coordInfo.Status == JobStatus.Cancelled)
            {
                throw new OperationFailedException(Core.Resources.UserRequestedCancellation, HttpStatusCode.BadRequest);
            }
            else if (coordInfo.Status == JobStatus.Failed)
            {
                var errorResult = JsonConvert.DeserializeObject<ImportJobErrorResult>(coordInfo.Result);
                if (errorResult.HttpStatusCode == 0)
                {
                    errorResult.HttpStatusCode = HttpStatusCode.InternalServerError;
                }

                // hide error message for InternalServerError
                var failureReason = errorResult.HttpStatusCode == HttpStatusCode.InternalServerError ? HttpStatusCode.InternalServerError.ToString() : errorResult.ErrorMessage;
                throw new OperationFailedException(string.Format(Core.Resources.OperationFailed, OperationsConstants.Import, failureReason), errorResult.HttpStatusCode);
            }
            else if (coordInfo.Status == JobStatus.Completed)
            {
                var start = Stopwatch.StartNew();
                var jobs = (await _queueClient.GetJobByGroupIdAsync(QueueType.Import, coordInfo.GroupId, true, cancellationToken)).Where(x => x.Id != coordInfo.Id).ToList();
                await Task.Delay(TimeSpan.FromSeconds(start.Elapsed.TotalSeconds * 10), cancellationToken); // throttle to avoid misuse.
                var inFlightJobsExist = jobs.Any(x => x.Status == JobStatus.Running || x.Status == JobStatus.Created);
                var cancelledJobsExist = jobs.Any(x => x.Status == JobStatus.Cancelled || (x.Status == JobStatus.Running && x.CancelRequested));
                var failedJobsExist = jobs.Any(x => x.Status == JobStatus.Failed);

                if (cancelledJobsExist && !failedJobsExist)
                {
                    throw new OperationFailedException(Core.Resources.UserRequestedCancellation, HttpStatusCode.BadRequest);
                }
                else if (failedJobsExist)
                {
                    var failed = jobs.First(x => x.Status == JobStatus.Failed);
                    var errorResult = JsonConvert.DeserializeObject<ImportJobErrorResult>(failed.Result);
                    if (errorResult.HttpStatusCode == 0)
                    {
                        errorResult.HttpStatusCode = HttpStatusCode.InternalServerError;
                    }

                    // hide error message for InternalServerError
                    var failureReason = errorResult.HttpStatusCode == HttpStatusCode.InternalServerError ? HttpStatusCode.InternalServerError.ToString() : errorResult.ErrorMessage;
                    throw new OperationFailedException(string.Format(Core.Resources.OperationFailed, OperationsConstants.Import, failureReason), errorResult.HttpStatusCode);
                }
                else // no failures here
                {
                    var coordResult = JsonConvert.DeserializeObject<ImportOrchestratorJobResult>(coordInfo.Result);
                    var results = GetProcessingResultAsync(jobs);
                    var result = new ImportJobResult()
                    {
                        Request = coordResult.Request,
                        TransactionTime = coordInfo.CreateDate,
                        Output = results.Completed,
                        Error = results.Failed,
                    };
                    return new GetImportResponse(!inFlightJobsExist ? HttpStatusCode.OK : HttpStatusCode.Accepted, result);
                }
            }
            else
            {
                throw new OperationFailedException(Core.Resources.UnknownError, HttpStatusCode.InternalServerError);
            }

            (List<ImportOperationOutcome> Completed, List<ImportFailedOperationOutcome> Failed) GetProcessingResultAsync(IList<JobInfo> jobs)
            {
                var completed = new List<ImportOperationOutcome>();
                var failed = new List<ImportFailedOperationOutcome>();
                foreach (var job in jobs.Where(_ => _.Status == JobStatus.Completed)) // not completed
                {
                    var definition = JsonConvert.DeserializeObject<ImportProcessingJobDefinition>(job.Definition);
                    var result = JsonConvert.DeserializeObject<ImportProcessingJobResult>(job.Result);
                    completed.Add(new ImportOperationOutcome() { Type = definition.ResourceType, Count = result.SucceededResources, InputUrl = new Uri(definition.ResourceLocation) });
                    if (result.FailedResources > 0)
                    {
                        failed.Add(new ImportFailedOperationOutcome() { Type = definition.ResourceType, Count = result.FailedResources, InputUrl = new Uri(definition.ResourceLocation), Url = result.ErrorLogLocation });
                    }
                }

                return (completed, failed);
            }
        }
    }
}
