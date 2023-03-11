// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Abstractions.Exceptions;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Messages.Delete;
using Microsoft.Health.JobManagement;
using static Microsoft.Health.JobManagement.JobExtensions;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Delete;

[JobTypeId((int)JobType.ConditionalDeleteProcessing)]
public class ConditionalDeleteProcessingJob : IJob
{
    private readonly RequestContextAccessor<IFhirRequestContext> _contextAccessor;
    private const int DeletePageSize = 1000;
    private readonly ISearchService _searchService;
    private readonly IMediator _mediator;

    public ConditionalDeleteProcessingJob(ISearchService searchService, IMediator mediator, RequestContextAccessor<IFhirRequestContext> contextAccessor)
    {
        _contextAccessor = contextAccessor;
        _searchService = EnsureArg.IsNotNull(searchService, nameof(searchService));
        _mediator = EnsureArg.IsNotNull(mediator, nameof(mediator));
    }

    public async Task<string> ExecuteAsync(JobInfo jobInfo, IProgress<string> progress, CancellationToken cancellationToken)
    {
        var request = jobInfo.DeserializeDefinition<ConditionalDeleteJobInfo>();
        var jobResult = jobInfo.DeserializeResult<ConditionalDeleteJobResult>();

        var fhirRequestContext = new FhirRequestContext(
            method: "ConditionalDelete",
            uriString: request.RequestUri.ToString(),
            baseUriString: request.BaseUri.ToString(),
            correlationId: request.ActivityId,
            requestHeaders: new Dictionary<string, StringValues>(),
            responseHeaders: new Dictionary<string, StringValues>())
        {
            IsBackgroundTask = true,
            Principal = DeserializeClaimsPrincipal(request), // Restore the original user context
        };

        _contextAccessor.RequestContext = fhirRequestContext;
        Activity.Current?.SetParentId(request.ActivityId);

        Tuple<string, string>[] readOnlyList = request.ConditionalParameters.ToArray();

        try
        {
            (IReadOnlyCollection<SearchResultEntry> matchedResults, string ct) =
                await _searchService.ConditionalSearchAsync(request.ResourceType, readOnlyList, DeletePageSize, cancellationToken);

            while (matchedResults.Any() || !string.IsNullOrEmpty(ct))
            {
                var tasks = matchedResults.SelectParallel(
                            async item => await _mediator.Send(new DeleteResourceRequest(request.ResourceType, item.Resource.ResourceId, request.DeleteOperation), cancellationToken), 4)
                    .ToArray();

                try
                {
                    await Task.WhenAll(tasks);
                }
                finally
                {
                    // report progress on successful deletes
                    foreach (Task<DeleteResourceResponse> result in tasks.Where(x => x.IsCompletedSuccessfully))
                    {
                        jobResult.TotalItemsDeleted += (await result).ResourcesDeleted;
                    }

                    progress.Report(jobResult);
                }

                if (!string.IsNullOrEmpty(ct))
                {
                    // Since we are deleting records, we can continue to fetch the first page unless there are no results
                    var ctWhenNoResults = matchedResults.Any() ? null : ct;

                    (matchedResults, ct) = await _searchService.ConditionalSearchAsync(
                        request.ResourceType,
                        readOnlyList,
                        DeletePageSize,
                        cancellationToken,
                        ctWhenNoResults);

                    jobResult.ContinuationToken = ctWhenNoResults;
                }
                else
                {
                    break;
                }
            }

            return SerializedResult(jobResult);
        }
        catch (Exception ex) when (ex is RequestRateExceededException)
        {
            throw new RetriableJobException(ex.Message, ex);
        }
    }

    private static ClaimsPrincipal DeserializeClaimsPrincipal(ConditionalDeleteJobInfo request)
    {
        using var stream = new MemoryStream(Convert.FromBase64String(request.Principal));
        using var reader = new BinaryReader(stream);
        var claimsPrincipal = new ClaimsPrincipal(reader);
        return claimsPrincipal;
    }
}
