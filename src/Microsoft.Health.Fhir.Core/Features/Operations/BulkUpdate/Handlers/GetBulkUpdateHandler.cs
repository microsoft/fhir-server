// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.Model;
using Hl7.Fhir.Support;
using MediatR;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Operations.BulkUpdate.Messages;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.JobManagement;

namespace Microsoft.Health.Fhir.Core.Features.Operations.BulkUpdate.Handlers
{
    public class GetBulkUpdateHandler : IRequestHandler<GetBulkUpdateRequest, GetBulkUpdateResponse>
    {
        private readonly IAuthorizationService<DataActions> _authorizationService;
        private readonly IQueueClient _queueClient;
        private const string ResourceUpdatedCountName = "ResourceUpdatedCount";
        private const string ResourceIgnoredCountName = "ResourceIgnoredCount";
        private const string ResourcePatchFailedCountName = "ResourcePatchFailedCount";

        public GetBulkUpdateHandler(
            IAuthorizationService<DataActions> authorizationService,
            IQueueClient queueClient)
        {
            _authorizationService = EnsureArg.IsNotNull(authorizationService, nameof(authorizationService));
            _queueClient = EnsureArg.IsNotNull(queueClient, nameof(queueClient));
        }

        public async Task<GetBulkUpdateResponse> Handle(GetBulkUpdateRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            if (await _authorizationService.CheckAccess(DataActions.BulkOperator, cancellationToken) != DataActions.BulkOperator)
            {
                throw new UnauthorizedFhirActionException();
            }

            var jobs = await _queueClient.GetJobByGroupIdAsync(QueueType.BulkUpdate, request.JobId, true, cancellationToken);

            if (jobs == null || jobs.Count == 0)
            {
                throw new JobNotFoundException(string.Format(Core.Resources.JobNotFound, request.JobId));
            }

            var failed = false;
            var cancelled = false;
            var succeeded = true;
            var resourcesUpdated = new Dictionary<string, long>();
            var resourcesIgnored = new Dictionary<string, long>();
            var resourcesPatchFailed = new Dictionary<string, long>();
            var issues = new List<OperationOutcomeIssue>();
            var failureResultCode = HttpStatusCode.OK;

            // check if any job still in created or running state
            bool isJobComplete = !jobs.Any(job => job.Status == JobStatus.Created || job.Status == JobStatus.Running);

            foreach (var job in jobs)
            {
                BulkUpdateResult result = null;
                try
                {
                    result = job.DeserializeResult<BulkUpdateResult>();
                }
                catch
                {
                    // Do nothing
                }

                if (job.Status == JobStatus.Failed)
                {
                    succeeded = false;
                    failed = true;
                    if (result != null)
                    {
                        foreach (var issue in result.Issues)
                        {
                            if (issue == "A task was canceled." && job.CancelRequested)
                            {
                                cancelled = true;
                            }
                            else
                            {
                                issues.Add(new OperationOutcomeIssue(OperationOutcomeConstants.IssueSeverity.Error, OperationOutcomeConstants.IssueType.Exception, detailsText: issue));
                            }
                        }
                    }
                    else
                    {
                        issues.Add(new OperationOutcomeIssue(OperationOutcomeConstants.IssueSeverity.Error, OperationOutcomeConstants.IssueType.Exception, detailsText: "Encountered an unhandled exception. The job will be marked as failed."));
                    }

                    // Need a way to get a failure result code. Most likely will be 503, 400, or 403
                    failureResultCode = HttpStatusCode.InternalServerError;
                }
                else if (job.Status == JobStatus.Cancelled)
                {
                    cancelled = true;
                    succeeded = false;
                }
                else if (job.Status != JobStatus.Completed)
                {
                    succeeded = false;
                }

                if (job.GetJobTypeId() == (int)JobType.BulkUpdateProcessing && result != null)
                {
                    long jobTotal = 0;

                    void UpdateResources(IDictionary<string, long> source, Dictionary<string, long> target)
                    {
                        foreach (var key in source.Keys)
                        {
                            jobTotal += source[key];
                            if (!target.TryAdd(key, source[key]))
                            {
                                target[key] += source[key];
                            }
                        }
                    }

                    UpdateResources(result.ResourcesUpdated, resourcesUpdated);
                    UpdateResources(result.ResourcesIgnored, resourcesIgnored);
                    UpdateResources(result.ResourcesPatchFailed, resourcesPatchFailed);
                }
            }

            var fhirResults = new List<Hl7.Fhir.Model.Parameters.ParameterComponent>();

            void AddParameterComponent(Dictionary<string, long> resourceDict, string resourceName)
            {
                if (resourceDict.Count > 0)
                {
                    var tuples = resourceDict
                        .Where(x => x.Value > 0)
                        .Select(x => Tuple.Create(x.Key, (DataType)new Integer64(x.Value)))
                        .ToArray();

                    if (tuples.Any())
                    {
                        var parameterComponent = new Hl7.Fhir.Model.Parameters.ParameterComponent
                        {
                            Name = resourceName,
                        };

                        foreach (var tuple in tuples)
                        {
                            parameterComponent.Part.Add(new Hl7.Fhir.Model.Parameters.ParameterComponent
                            {
                                Name = tuple.Item1,
                                Value = tuple.Item2,
                            });
                        }

                        fhirResults.Add(parameterComponent);
                    }
                }
            }

            AddParameterComponent(resourcesUpdated, ResourceUpdatedCountName);
            AddParameterComponent(resourcesIgnored, ResourceIgnoredCountName);
            AddParameterComponent(resourcesPatchFailed, ResourcePatchFailedCountName);

            HttpStatusCode statusCode;
            if (failed && issues.Count > 0)
            {
                statusCode = failureResultCode;
            }
            else if (cancelled)
            {
                issues.Add(new OperationOutcomeIssue(
                    OperationOutcomeConstants.IssueSeverity.Warning,
                    OperationOutcomeConstants.IssueType.Informational,
                    detailsText: "Job Canceled"));
                statusCode = HttpStatusCode.OK;
            }
            else if (failed)
            {
                issues.Add(new OperationOutcomeIssue(
                    OperationOutcomeConstants.IssueSeverity.Error,
                    OperationOutcomeConstants.IssueType.Exception,
                    detailsText: "Encountered an unhandled exception. The job will be marked as failed."));

                // if failed and no issues and if patchFailedCount and job is still running then soft failed job retun 202 until enitre job is complete
                statusCode = resourcesPatchFailed.Any() && !isJobComplete ? HttpStatusCode.Accepted : failureResultCode;
            }
            else if (succeeded)
            {
                statusCode = HttpStatusCode.OK;
            }
            else
            {
                statusCode = HttpStatusCode.Accepted;
            }

            if (!cancelled && !isJobComplete)
            {
                issues.Add(new OperationOutcomeIssue(
                    OperationOutcomeConstants.IssueSeverity.Information,
                    OperationOutcomeConstants.IssueType.Informational,
                    detailsText: "Job In Progress"));
            }

            if (resourcesPatchFailed.Count > 0)
            {
                issues.Add(new OperationOutcomeIssue(OperationOutcomeConstants.IssueSeverity.Error, OperationOutcomeConstants.IssueType.Exception, detailsText: "Please use FHIR Patch endpoint for detailed error on Patch failed resources."));
            }

            return new GetBulkUpdateResponse(fhirResults, issues, statusCode);
        }
    }
}
