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
using Microsoft.Health.Fhir.Core.Features.Operations.BulkDelete.Messages;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.JobManagement;

namespace Microsoft.Health.Fhir.Core.Features.Operations.BulkDelete.Handlers
{
    public class GetBulkDeleteHandler : IRequestHandler<GetBulkDeleteRequest, GetBulkDeleteResponse>
    {
        private readonly IAuthorizationService<DataActions> _authorizationService;
        private readonly IQueueClient _queueClient;
        private const string ResourceDeletedCountName = "ResourceDeletedCount";

        public GetBulkDeleteHandler(
            IAuthorizationService<DataActions> authorizationService,
            IQueueClient queueClient)
        {
            _authorizationService = EnsureArg.IsNotNull(authorizationService, nameof(authorizationService));
            _queueClient = EnsureArg.IsNotNull(queueClient, nameof(queueClient));
        }

        public async Task<GetBulkDeleteResponse> Handle(GetBulkDeleteRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            if (await _authorizationService.CheckAccess(DataActions.Read, cancellationToken) != DataActions.Read)
            {
                throw new UnauthorizedFhirActionException();
            }

            var jobs = await _queueClient.GetJobByGroupIdAsync(QueueType.BulkDelete, request.JobId, true, cancellationToken);

            if (jobs == null || jobs.Count == 0)
            {
                throw new JobNotFoundException(string.Format(Core.Resources.JobNotFound, request.JobId));
            }

            var failed = false;
            var cancelled = false;
            var succeeded = true;
            var resourcesDeleted = new Dictionary<string, long>();
            var issues = new List<OperationOutcomeIssue>();
            var failureResultCode = HttpStatusCode.OK;

            foreach (var job in jobs)
            {
                BulkDeleteResult result = null;
                try
                {
                    result = job.DeserializeResult<BulkDeleteResult>();
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

                if (job.GetJobTypeId() == (int)JobType.BulkDeleteProcessing && result != null)
                {
                    foreach (var key in result.ResourcesDeleted.Keys)
                    {
                        if (!resourcesDeleted.TryAdd(key, result.ResourcesDeleted[key]))
                        {
                            resourcesDeleted[key] += result.ResourcesDeleted[key];
                        }
                    }
                }
            }

            var fhirResults = new List<Parameters.ParameterComponent>();

            if (resourcesDeleted.Count > 0)
            {
                Tuple<string, DataType>[] tuples = resourcesDeleted
                    .Where(x => x.Value > 0)
                    .Select(x => Tuple.Create(x.Key, (DataType)new FhirDecimal(x.Value)))
                    .ToArray();

                if (tuples.Any())
                {
                    var parameterComponent = new Parameters.ParameterComponent
                    {
                        Name = ResourceDeletedCountName,
                    };

                    foreach (var tuple in tuples)
                    {
                        parameterComponent.Part.Add(new Parameters.ParameterComponent
                        {
                            Name = tuple.Item1,
                            Value = tuple.Item2,
                        });
                    }

                    fhirResults.Add(parameterComponent);
                }
            }

            if (failed && issues.Count > 0)
            {
                return new GetBulkDeleteResponse(fhirResults, issues, failureResultCode);
            }
            else if (cancelled)
            {
                issues.Add(new OperationOutcomeIssue(
                    OperationOutcomeConstants.IssueSeverity.Warning,
                    OperationOutcomeConstants.IssueType.Informational,
                    detailsText: "Job Canceled"));
                return new GetBulkDeleteResponse(fhirResults, issues, HttpStatusCode.OK);
            }
            else if (failed)
            {
                issues.Add(new OperationOutcomeIssue(OperationOutcomeConstants.IssueSeverity.Error, OperationOutcomeConstants.IssueType.Exception, detailsText: "Encountered an unhandled exception. The job will be marked as failed."));
                return new GetBulkDeleteResponse(fhirResults, issues, failureResultCode);
            }
            else if (succeeded)
            {
                return new GetBulkDeleteResponse(fhirResults, null, HttpStatusCode.OK);
            }
            else
            {
                issues.Add(new OperationOutcomeIssue(
                    OperationOutcomeConstants.IssueSeverity.Information,
                    OperationOutcomeConstants.IssueType.Informational,
                    detailsText: "Job In Progress"));
                return new GetBulkDeleteResponse(fhirResults, issues, HttpStatusCode.Accepted);
            }
        }
    }
}
