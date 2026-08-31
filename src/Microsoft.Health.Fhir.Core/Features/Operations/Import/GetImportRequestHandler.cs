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
using Medino;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;
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

        public async Task<GetImportResponse> HandleAsync(GetImportRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            await _authorizationService.CheckAccess(DataActions.Import, true, cancellationToken);

            var coord = await _queueClient.GetJobByIdAsync(QueueType.Import, request.JobId, false, cancellationToken);
            if (coord == null || coord.Status == JobStatus.Archived)
            {
                throw new ResourceNotFoundException(string.Format(Core.Resources.ImportJobNotFound, request.JobId));
            }
            else if (coord.Status == JobStatus.Created || coord.Status == JobStatus.Running)
            {
                return new GetImportResponse(HttpStatusCode.Accepted);
            }
            else if (coord.Status == JobStatus.Cancelled)
            {
                throw new OperationFailedException(Core.Resources.UserRequestedCancellation, HttpStatusCode.BadRequest);
            }
            else if (coord.Status == JobStatus.Failed)
            {
                var errorResult = DeserializeOrDefault<ImportJobErrorResult>(coord.Result);
                if (errorResult.HttpStatusCode == 0)
                {
                    errorResult.HttpStatusCode = HttpStatusCode.InternalServerError;
                }

                // hide error message for InternalServerError
                var failureReason = errorResult.HttpStatusCode == HttpStatusCode.InternalServerError ? HttpStatusCode.InternalServerError.ToString() : errorResult.ErrorMessage;
                throw new OperationFailedException(string.Format(Core.Resources.OperationFailed, OperationsConstants.Import, failureReason), errorResult.HttpStatusCode);
            }
            else if (coord.Status == JobStatus.Completed)
            {
                var start = Stopwatch.StartNew();
                var jobs = (await _queueClient.GetJobByGroupIdAsync(QueueType.Import, coord.GroupId, true, cancellationToken)).Where(x => x.Id != coord.Id).ToList();
                var results = GetProcessingResultAsync(jobs, request.ReturnDetails);
                await Task.Delay(TimeSpan.FromSeconds(start.Elapsed.TotalSeconds > 6 ? 60 : start.Elapsed.TotalSeconds * 10), cancellationToken); // throttle to avoid misuse.
                var inFlightJobsExist = jobs.Any(x => x.Status == JobStatus.Running || x.Status == JobStatus.Created);
                var cancelledJobsExist = jobs.Any(x => x.Status == JobStatus.Cancelled || x.CancelRequested);
                var failedJobsExist = jobs.Any(x => x.Status == JobStatus.Failed && !x.CancelRequested);

                if (cancelledJobsExist && !failedJobsExist)
                {
                    throw new OperationFailedException(Core.Resources.UserRequestedCancellation, HttpStatusCode.BadRequest);
                }
                else if (failedJobsExist)
                {
                    var failed = jobs.First(x => x.Status == JobStatus.Failed && !x.CancelRequested);
                    var errorResult = DeserializeOrDefault<ImportJobErrorResult>(failed.Result);
                    if (errorResult.HttpStatusCode == 0)
                    {
                        errorResult.HttpStatusCode = HttpStatusCode.InternalServerError;
                    }

                    // hide error message for InternalServerError
                    var failureReason = errorResult.HttpStatusCode == HttpStatusCode.InternalServerError ? HttpStatusCode.InternalServerError.ToString() : errorResult.ErrorMessage;

                    // The input file location is not available on every job record in the group, so the error file cannot always be reported.
                    var message = TryGetProcessingJobInput(failed, out _, out var resourceLocation)
                        ? string.Format(Core.Resources.OperationFailedWithErrorFile, OperationsConstants.Import, failureReason, resourceLocation.OriginalString)
                        : string.Format(Core.Resources.OperationFailed, OperationsConstants.Import, failureReason);

                    throw new OperationFailedException(message, errorResult.HttpStatusCode);
                }
                else // no failures here
                {
                    var coordResult = DeserializeOrDefault<ImportOrchestratorJobResult>(coord.Result);
                    var result = new ImportJobResult() { Request = coordResult.Request, TransactionTime = coord.CreateDate, Output = results.Completed, Error = results.Failed };
                    return new GetImportResponse(!inFlightJobsExist ? HttpStatusCode.OK : HttpStatusCode.Accepted, result);
                }
            }
            else
            {
                throw new OperationFailedException(Core.Resources.UnknownError, HttpStatusCode.InternalServerError);
            }

            static (List<ImportOperationOutcome> Completed, List<ImportFailedOperationOutcome> Failed) GetProcessingResultAsync(IList<JobInfo> jobs, bool returnDetails)
            {
                var completed = new List<ImportOperationOutcome>();
                var failed = new List<ImportFailedOperationOutcome>();
                foreach (var job in jobs.Where(_ => _.Status == JobStatus.Completed))
                {
                    // The job group also contains the orchestrator job, which is returned here whenever status is requested
                    // by a job id other than the orchestrator's. It records no input file url, and neither does a job whose
                    // state was not persisted. Such records cannot be reported as a processed input file, so they are
                    // skipped rather than allowed to fail the whole status request.
                    if (!TryGetProcessingJobInput(job, out var definition, out var inputUrl) || string.IsNullOrWhiteSpace(job.Result))
                    {
                        continue;
                    }

                    var result = DeserializeOrDefault<ImportProcessingJobResult>(job.Result);
                    completed.Add(new ImportOperationOutcome() { Type = definition.ResourceType, Count = result.SucceededResources, InputUrl = inputUrl });
                    if (result.FailedResources > 0)
                    {
                        failed.Add(new ImportFailedOperationOutcome() { Type = definition.ResourceType, Count = result.FailedResources, InputUrl = inputUrl, Url = result.ErrorLogLocation });
                    }
                }

                if (returnDetails)
                {
                    return (completed, failed);
                }

                // group success results by url
                var groupped = completed.GroupBy(o => o.InputUrl).Select(g => new ImportOperationOutcome() { Type = g.First().Type, Count = g.Sum(_ => _.Count), InputUrl = g.Key }).ToList();

                return (groupped, failed);
            }
        }

        /// <summary>
        /// Resolves the import processing job definition and the url of the input file it processed.
        /// </summary>
        /// <param name="job">The job to inspect.</param>
        /// <param name="definition">When this method returns true, the import processing job definition; otherwise null.</param>
        /// <param name="inputUrl">When this method returns true, the url of the processed input file; otherwise null.</param>
        /// <returns>True when the job records the url of an input file it processed; otherwise false.</returns>
        private static bool TryGetProcessingJobInput(JobInfo job, out ImportProcessingJobDefinition definition, out Uri inputUrl)
        {
            // Recording an input file url is what separates a processing job from the orchestrator job, whose definition has
            // no such property. Matching on the url rather than on the job type keeps every record the status response
            // reported before, including any written before job types were persisted.
            definition = DeserializeOrDefault<ImportProcessingJobDefinition>(job.Definition);

            if (!Uri.TryCreate(definition.ResourceLocation, UriKind.Absolute, out inputUrl))
            {
                definition = null;
                inputUrl = null;
                return false;
            }

            return true;
        }

        /// <summary>
        /// Deserializes persisted job state, returning a default instance when the state is absent or null valued.
        /// </summary>
        /// <typeparam name="T">The type of the persisted job state.</typeparam>
        /// <param name="json">The persisted job state.</param>
        /// <returns>The deserialized job state, or a default instance.</returns>
        private static T DeserializeOrDefault<T>(string json)
            where T : new()
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new T();
            }

            return JsonConvert.DeserializeObject<T>(json) ?? new T();
        }
    }
}
