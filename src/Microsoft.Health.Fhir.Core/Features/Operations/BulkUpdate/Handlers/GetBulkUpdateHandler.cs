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
using Hl7.Fhir.Model;
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
        private const string TotalResourcesCountName = "TotalResourcesCountName";
        private const string ResourceUpdatedCountName = "ResourceUpdatedCount";
        private const string ResourceIgnoredCountName = "ResourceIgnoredCountName";
        private const string ResourcePatchFailedCountName = "ResourcePatchFailedCountName";

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
            var totalResources = new Dictionary<string, long>();
            var resourcesUpdated = new Dictionary<string, long>();
            var resourcesIgnored = new Dictionary<string, long>();
            var resourcesPatchFailed = new Dictionary<string, long>();
            var issues = new List<OperationOutcomeIssue>();
            var failureResultCode = HttpStatusCode.OK;

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
                    failed = true;
                    succeeded = false;

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

                    void UpdateResources(Dictionary<string, long> source, Dictionary<string, long> target)
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

                    UpdateResources((Dictionary<string, long>)result.TotalResources, totalResources);
                    UpdateResources((Dictionary<string, long>)result.ResourcesUpdated, resourcesUpdated);
                    UpdateResources((Dictionary<string, long>)result.ResourcesIgnored, resourcesIgnored);
                    UpdateResources((Dictionary<string, long>)result.ResourcesPatchFailed, resourcesPatchFailed);
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

            AddParameterComponent(totalResources, TotalResourcesCountName);
            AddParameterComponent(resourcesUpdated, ResourceUpdatedCountName);
            AddParameterComponent(resourcesIgnored, ResourceIgnoredCountName);
            AddParameterComponent(resourcesPatchFailed, ResourcePatchFailedCountName);

            if (failed && issues.Count > 0)
            {
                return new GetBulkUpdateResponse(fhirResults, issues, failureResultCode);
            }
            else if (cancelled)
            {
                issues.Add(new OperationOutcomeIssue(
                    OperationOutcomeConstants.IssueSeverity.Warning,
                    OperationOutcomeConstants.IssueType.Informational,
                    detailsText: "Job Canceled"));
                return new GetBulkUpdateResponse(fhirResults, issues, HttpStatusCode.OK);
            }
            else if (failed)
            {
                issues.Add(new OperationOutcomeIssue(OperationOutcomeConstants.IssueSeverity.Error, OperationOutcomeConstants.IssueType.Exception, detailsText: "Encountered an unhandled exception. The job will be marked as failed."));
                return new GetBulkUpdateResponse(fhirResults, issues, failureResultCode);
            }
            else if (succeeded)
            {
                return new GetBulkUpdateResponse(fhirResults, issues, HttpStatusCode.OK);
            }
            else
            {
                issues.Add(new OperationOutcomeIssue(
                    OperationOutcomeConstants.IssueSeverity.Information,
                    OperationOutcomeConstants.IssueType.Informational,
                    detailsText: "Job In Progress"));
                return new GetBulkUpdateResponse(fhirResults, issues, HttpStatusCode.Accepted);
            }
        }
    }
}
