// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Operations.BulkDelete.Messages;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Messages.Delete;
using Microsoft.Health.JobManagement;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Operations.BulkDelete
{
    [JobTypeId((int)JobType.BulkDeleteProcessing)]
    public class BulkDeleteProcessingJob : IJob
    {
        private readonly Func<IScoped<IDeletionService>> _deleterFactory;
        private readonly RequestContextAccessor<IFhirRequestContext> _contextAccessor;
        private readonly IMediator _mediator;
        private readonly ISearchService _searchService;
        private readonly IQueueClient _queueClient;

        public BulkDeleteProcessingJob(
            Func<IScoped<IDeletionService>> deleterFactory,
            RequestContextAccessor<IFhirRequestContext> contextAccessor,
            IMediator mediator,
            ISearchService searchService,
            IQueueClient queueClient)
        {
            _deleterFactory = EnsureArg.IsNotNull(deleterFactory, nameof(deleterFactory));
            _contextAccessor = EnsureArg.IsNotNull(contextAccessor, nameof(contextAccessor));
            _mediator = EnsureArg.IsNotNull(mediator, nameof(mediator));
            _searchService = EnsureArg.IsNotNull(searchService, nameof(searchService));
            _queueClient = EnsureArg.IsNotNull(queueClient, nameof(queueClient));
        }

        public async Task<string> ExecuteAsync(JobInfo jobInfo, IProgress<string> progress, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(jobInfo, nameof(jobInfo));
            EnsureArg.IsNotNull(progress, nameof(progress));

            IFhirRequestContext existingFhirRequestContext = _contextAccessor.RequestContext;

            try
            {
                BulkDeleteDefinition definition = jobInfo.DeserializeDefinition<BulkDeleteDefinition>();

                Activity.Current?.SetParentId(definition.ParentRequestId);

                var fhirRequestContext = new FhirRequestContext(
                    method: "BulkDelete",
                    uriString: definition.Url,
                    baseUriString: definition.BaseUrl,
                    correlationId: jobInfo.Id.ToString() + '-' + jobInfo.GroupId.ToString(),
                    requestHeaders: new Dictionary<string, StringValues>(),
                    responseHeaders: new Dictionary<string, StringValues>())
                {
                    IsBackgroundTask = true,
                };

                _contextAccessor.RequestContext = fhirRequestContext;
                var result = new BulkDeleteResult();
                long numDeleted;
                using IScoped<IDeletionService> deleter = _deleterFactory.Invoke();
                Exception exception = null;
                List<string> types = definition.Type.SplitByOrSeparator().ToList();

                try
                {
                    numDeleted = await deleter.Value.DeleteMultipleAsync(
                        new ConditionalDeleteResourceRequest(
                            types[0],
                            (IReadOnlyList<Tuple<string, string>>)definition.SearchParameters,
                            definition.DeleteOperation,
                            maxDeleteCount: null,
                            deleteAll: true,
                            versionType: definition.VersionType),
                        cancellationToken);
                }
                catch (IncompleteOperationException<long> ex)
                {
                    numDeleted = ex.PartialResults;
                    result.Issues.Add(ex.Message);
                    exception = ex;
                }

                result.ResourcesDeleted.Add(definition.Type, numDeleted);

                await _mediator.Publish(new BulkDeleteMetricsNotification(jobInfo.Id, numDeleted), cancellationToken);

                if (exception != null)
                {
                    var jobException = new JobExecutionException($"Exception encounted while deleting resources: {result.Issues.First()}", result, exception);
                    jobException.RequestCancellationOnFailure = true;
                    throw jobException;
                }

                if (types.Count > 1)
                {
                    types.RemoveAt(0);
                    BulkDeleteDefinition processingDefinition = null;
                    while (types.Count > 0)
                    {
                        int numResources = (await _searchService.SearchAsync(types[0], (IReadOnlyList<Tuple<string, string>>)definition.SearchParameters, cancellationToken, resourceVersionTypes: definition.VersionType)).TotalCount.GetValueOrDefault();

                        if (numResources == 0)
                        {
                            types.RemoveAt(0);
                            continue;
                        }

                        string resourceType = types.JoinByOrSeparator();

                        processingDefinition = new BulkDeleteDefinition(
                            JobType.BulkDeleteProcessing,
                            definition.DeleteOperation,
                            resourceType,
                            definition.SearchParameters,
                            definition.Url,
                            definition.BaseUrl,
                            definition.ParentRequestId,
                            numResources,
                            definition.VersionType);

                        await _queueClient.EnqueueAsync(QueueType.BulkDelete, cancellationToken, jobInfo.GroupId, definitions: processingDefinition);
                        break;
                    }
                }

                return JsonConvert.SerializeObject(result);
            }
            finally
            {
                _contextAccessor.RequestContext = existingFhirRequestContext;
            }
        }
    }
}
