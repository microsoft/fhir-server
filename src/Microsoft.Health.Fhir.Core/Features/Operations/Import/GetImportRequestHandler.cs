// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
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
        private const int MaxDetailedJobs = 100;

        private readonly IQueueClient _queueClient;
        private readonly IAuthorizationService<DataActions> _authorizationService;

        public GetImportRequestHandler(
            IQueueClient queueClient,
            IAuthorizationService<DataActions> authorizationService)
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

            // The orchestrator definition identifies in-memory test imports and controls whether execution
            // statistics are included in the completed status response.
            var coord = await _queueClient.GetJobByIdAsync(QueueType.Import, request.JobId, true, cancellationToken);
            if (coord == null || coord.Id != coord.GroupId || coord.Status == JobStatus.Archived) // if job is processing one -> reject
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
                var errorResult = JsonConvert.DeserializeObject<ImportJobErrorResult>(coord.Result);
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
                var coordDefinition = JsonConvert.DeserializeObject<ImportOrchestratorJobDefinition>(coord.Definition);
                var suppressSuccessfulOutput = coordDefinition.InMemoryTestProcessingJobs > 0;
                var jobs = (await _queueClient.GetJobByGroupIdAsync(QueueType.Import, coord.GroupId, true, cancellationToken)).Where(x => x.Id != coord.Id).ToList();
                var (completedOutcomes, failedOutcomes, jobResultsById) = GetProcessingResultAsync(jobs, request.ReturnDetails, suppressSuccessfulOutput);
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
                    var errorResult = JsonConvert.DeserializeObject<ImportJobErrorResult>(failed.Result);
                    var definition = JsonConvert.DeserializeObject<ImportProcessingJobDefinition>(failed.Definition);
                    if (errorResult.HttpStatusCode == 0)
                    {
                        errorResult.HttpStatusCode = HttpStatusCode.InternalServerError;
                    }

                    var resourceLocation = new Uri(definition.ResourceLocation);

                    // hide error message for InternalServerError
                    var failureReason = errorResult.HttpStatusCode == HttpStatusCode.InternalServerError ? HttpStatusCode.InternalServerError.ToString() : errorResult.ErrorMessage;

                    throw new OperationFailedException(string.Format(Core.Resources.OperationFailedWithErrorFile, OperationsConstants.Import, failureReason, resourceLocation.OriginalString), errorResult.HttpStatusCode);
                }
                else // no failures here
                {
                    var coordResult = JsonConvert.DeserializeObject<ImportOrchestratorJobResult>(coord.Result);
                    var result = new ImportJobResult() { Request = coordResult.Request, TransactionTime = coord.CreateDate, Output = completedOutcomes, Error = failedOutcomes };

                    // Include per-job execution stats only for in-memory test imports (for CPU profiling)
                    if (coordDefinition.InMemoryTestProcessingJobs > 0)
                    {
                        var jobLines = jobs.Select(job => jobResultsById.TryGetValue(job.Id, out var result) ? new { Job = job, Result = result } : null).Where(_ => _ != null)
                            .OrderByDescending(_ => _.Job.StartDate.Value)
                            .Select(_ =>
                            {
                                var clockMilliseconds = _.Result.ClockMilliseconds;
                                var databaseMilliseconds = _.Result.DatabaseMilliseconds;
                                var cpuMilliseconds = clockMilliseconds - databaseMilliseconds; // x - null = null
                                return new
                                {
                                    Line = $"job={_.Job.Id} cpu_msec={cpuMilliseconds} clock_msec={clockMilliseconds} database_msec={databaseMilliseconds}",
                                    CpuMilliseconds = cpuMilliseconds,
                                    ClockMilliseconds = clockMilliseconds,
                                    DatabaseMilliseconds = databaseMilliseconds,
                                    StartDate = _.Job.StartDate.Value,
                                    EndDate = _.Job.EndDate.Value,
                                };
                            })
                            .ToList();

                        if (jobLines.Count > 0)
                        {
                            var retriedJobs = jobLines.Count(x => x.DatabaseMilliseconds is null);
                            var jobmsec = jobLines.Sum(_ => (_.EndDate - _.StartDate).TotalMilliseconds);
                            var elapsedmsec = (jobLines.Max(_ => _.EndDate) - jobLines.Min(_ => _.StartDate)).TotalMilliseconds;
                            var parallelism = elapsedmsec > 0 ? Math.Round(jobmsec / elapsedmsec, 2) : 0;
                            var totalCpuMilliseconds = jobLines.Sum(_ => _.CpuMilliseconds);
                            var cpuMillisecondsPerResource = totalCpuMilliseconds.HasValue ? Math.Round((double)totalCpuMilliseconds.Value / jobLines.Count / 1000, 2) : (double?)null;
                            var executionStats = new List<string> { $"jobs={jobLines.Count} cpu_msec_per_resource={cpuMillisecondsPerResource:F2} clock_msec={jobLines.Sum(_ => _.ClockMilliseconds)} database_msec={jobLines.Sum(_ => _.DatabaseMilliseconds)} retried_jobs={retriedJobs} parallelism={parallelism:F2}" };
                            executionStats.AddRange(jobLines.Select(x => x.Line));

                            result.ExecutionStats = executionStats;
                        }
                    }

                    return new GetImportResponse(!inFlightJobsExist ? HttpStatusCode.OK : HttpStatusCode.Accepted, result);
                }
            }
            else
            {
                throw new OperationFailedException(Core.Resources.UnknownError, HttpStatusCode.InternalServerError);
            }

            static (List<ImportOperationOutcome> Completed, List<ImportFailedOperationOutcome> Failed, Dictionary<long, ImportProcessingJobResult> JobResultsById) GetProcessingResultAsync(IList<JobInfo> jobs, bool returnDetails, bool suppressSuccessfulOutput)
            {
                var completed = new List<ImportOperationOutcome>();
                var failed = new List<ImportFailedOperationOutcome>();
                var jobResultsById = new Dictionary<long, ImportProcessingJobResult>();
                IEnumerable<JobInfo> completedJobs = jobs.Where(_ => _.Status == JobStatus.Completed).OrderBy(_ => _.StartDate).ThenBy(_ => _.Id);
                if (returnDetails)
                {
                    completedJobs = completedJobs.Take(MaxDetailedJobs);
                }

                foreach (var job in completedJobs)
                {
                    var definition = JsonConvert.DeserializeObject<ImportProcessingJobDefinition>(job.Definition);
                    var result = JsonConvert.DeserializeObject<ImportProcessingJobResult>(job.Result);
                    jobResultsById[job.Id] = result;

                    if (!suppressSuccessfulOutput)
                    {
                        completed.Add(new ImportOperationOutcome() { Type = definition.ResourceType, Count = result.SucceededResources, InputUrl = new Uri(definition.ResourceLocation) });
                    }

                    if (result.FailedResources > 0)
                    {
                        failed.Add(new ImportFailedOperationOutcome() { Type = definition.ResourceType, Count = result.FailedResources, InputUrl = new Uri(definition.ResourceLocation), Url = result.ErrorLogLocation });
                    }
                }

                if (returnDetails)
                {
                    return (completed, failed, jobResultsById);
                }

                // group success results by url
                var groupped = completed.GroupBy(o => o.InputUrl).Select(g => new ImportOperationOutcome() { Type = g.First().Type, Count = g.Sum(_ => _.Count), InputUrl = g.Key }).ToList();

                return (groupped, failed, jobResultsById);
            }
        }
    }
}
