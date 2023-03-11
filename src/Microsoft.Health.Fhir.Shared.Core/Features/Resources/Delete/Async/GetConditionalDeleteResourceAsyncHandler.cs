// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.Model;
using MediatR;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Delete;
using Microsoft.Health.Fhir.Core.Messages.Delete;
using Microsoft.Health.JobManagement;

namespace Microsoft.Health.Fhir.Core.Features.Resources.Delete.Async;

public class GetConditionalDeleteResourceAsyncHandler : IRequestHandler<GetConditionalDeleteResourceAsyncRequest, GetConditionalDeleteResourceAsyncResponse>
{
    private readonly RequestContextAccessor<IFhirRequestContext> _requestContextAccessor;
    private readonly IQueueClient _queueClient;

    public GetConditionalDeleteResourceAsyncHandler(IQueueClient queueClient, RequestContextAccessor<IFhirRequestContext> requestContextAccessor)
    {
        _requestContextAccessor = EnsureArg.IsNotNull(requestContextAccessor, nameof(requestContextAccessor));
        _queueClient = EnsureArg.IsNotNull(queueClient, nameof(queueClient));
    }

    /// <summary>
    /// Async operation response in respect to http://build.fhir.org/async-bundle.html#3.2.6.2.3
    /// </summary>
    public async Task<GetConditionalDeleteResourceAsyncResponse> Handle(GetConditionalDeleteResourceAsyncRequest request, CancellationToken cancellationToken)
    {
        EnsureArg.IsNotNull(request, nameof(request));

        IReadOnlyList<JobInfo> job = await _queueClient.GetJobByGroupIdAsync((int)QueueType.ConditionalDelete, request.GroupId, true, cancellationToken);

        JobInfo definitionJob = job.FirstOrDefault(x => x.GetJobTypeId() == (int)JobType.ConditionalDeleteOrchestrator);
        ConditionalDeleteJobInfo definition = definitionJob.DeserializeDefinition<ConditionalDeleteJobInfo>();

        JobInfo[] processingJobs = job
            .Where(x => x.GetJobTypeId() == (int)JobType.ConditionalDeleteProcessing)
            .ToArray();

        var result = processingJobs
            .Where(x => x.Status == JobStatus.Completed || x.Status == JobStatus.Running)
            .Select(x => x.DeserializeResult<ConditionalDeleteJobResult>())
            .Sum(x => x.TotalItemsDeleted);

        JobErrorInfo[] failedResults = processingJobs
            .Where(x => x.Status == JobStatus.Failed)
            .Select(x => x.DeserializeResult<JobErrorInfo>())
            .ToArray();

        bool isTerminal = true;
        HttpStatusCode status;

        if (failedResults.Any() || definitionJob?.Status == JobStatus.Failed)
        {
            status = HttpStatusCode.BadRequest;

            var operationOutcome = new OperationOutcome
            {
                Id = definition.ActivityId,
                Issue = failedResults.Select(x => new OperationOutcome.IssueComponent
                {
                    Severity = OperationOutcome.IssueSeverity.Error,
                    Code = OperationOutcome.IssueType.Exception,
                    Diagnostics = x.Message,
                }).ToList(),
            };

            if (definitionJob?.Status == JobStatus.Failed)
            {
                operationOutcome.Issue.Add(new OperationOutcome.IssueComponent
                {
                    Severity = OperationOutcome.IssueSeverity.Error,
                    Code = OperationOutcome.IssueType.Exception,
                    Diagnostics = definitionJob.DeserializeResult<JobErrorInfo>().Message,
                });
            }

            // on error, only the operation outcome is returned
            return new GetConditionalDeleteResourceAsyncResponse(operationOutcome, status, true);
        }

        if (job.Any(x => x.Status == JobStatus.Cancelled))
        {
            throw new ResourceNotFoundException(string.Format(Core.Resources.JobNotFound, request.GroupId));
        }

        if (job.All(x => x.Status == JobStatus.Completed || x.Status == JobStatus.Archived))
        {
            status = HttpStatusCode.OK;
        }
        else
        {
            if (job.Any(x => x.Status == JobStatus.Running))
            {
                // In-progress messages can be returned via X-Status header
                _requestContextAccessor.RequestContext.ResponseHeaders.Add("X-Status", $"in progress, items deleted: {result}");
            }

            status = HttpStatusCode.Accepted;
            isTerminal = false;
        }

        var outcome = new Parameters { { "items-deleted", new Integer64(result) } };
        outcome.Id = request.GroupId.ToString();

        var responseBundle = new Hl7.Fhir.Model.Bundle
        {
            Id = definition.ActivityId,
            Type = Hl7.Fhir.Model.Bundle.BundleType.BatchResponse,
            Entry = new List<Hl7.Fhir.Model.Bundle.EntryComponent>
            {
                new()
                {
                    Response = new Hl7.Fhir.Model.Bundle.ResponseComponent
                    {
                        Status = $"{(int)status} {status}",
                        Outcome = outcome,
                    },
                },
            },
        };

        return new GetConditionalDeleteResourceAsyncResponse(responseBundle, status, isTerminal);
    }
}
