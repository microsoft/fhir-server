﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.BulkDelete;
using Microsoft.Health.Fhir.Core.Features.Operations.BulkDelete.Mediator;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Messages.Delete;
using Microsoft.Health.JobManagement;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Api.Features.Operations.BulkDelete
{
    [JobTypeId((int)JobType.BulkDeleteProcessing)]
    public class BulkDeleteProcessingJob : IJob
    {
        private readonly Func<IScoped<IDeletionService>> _deleterFactory;
        private readonly RequestContextAccessor<IFhirRequestContext> _contextAccessor;
        private readonly IMediator _mediator;

        public BulkDeleteProcessingJob(
            Func<IScoped<IDeletionService>> deleterFactory,
            RequestContextAccessor<IFhirRequestContext> contextAccessor,
            IMediator mediator)
        {
            _deleterFactory = EnsureArg.IsNotNull(deleterFactory, nameof(deleterFactory));
            _contextAccessor = EnsureArg.IsNotNull(contextAccessor, nameof(contextAccessor));
            _mediator = EnsureArg.IsNotNull(mediator, nameof(mediator));
        }

        public async Task<string> ExecuteAsync(JobInfo jobInfo, IProgress<string> progress, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(jobInfo, nameof(jobInfo));
            EnsureArg.IsNotNull(progress, nameof(progress));

            var existingFhirRequestContext = _contextAccessor.RequestContext;

            try
            {
                BulkDeleteDefinition definition = jobInfo.DeserializeDefinition<BulkDeleteDefinition>();

                var fhirRequestContext = new FhirRequestContext(
                    method: "BulkDelete",
                    uriString: definition.Url,
                    baseUriString: definition.BaseUrl,
                    correlationId: jobInfo.Id.ToString(),
                    requestHeaders: new Dictionary<string, StringValues>(),
                    responseHeaders: new Dictionary<string, StringValues>())
                {
                    IsBackgroundTask = true,
                };

                _contextAccessor.RequestContext = fhirRequestContext;
                var result = new BulkDeleteResult();
                long itemsDeleted;
                using IScoped<IDeletionService> deleter = _deleterFactory.Invoke();

                try
                {
                    itemsDeleted = await deleter.Value.DeleteMultipleAsync(
                        new ConditionalDeleteResourceRequest(
                            definition.Type,
                            (IReadOnlyList<Tuple<string, string>>)definition.SearchParameters,
                            definition.DeleteOperation,
                            maxDeleteCount: null,
                            deleteAll: true),
                        cancellationToken);
                }
                catch (PartialSuccessException<long> ex)
                {
                    itemsDeleted = ex.PartialResults;
                    result.Issues.Add(ex);
                }

                result.ResourcesDeleted.Add(definition.Type, itemsDeleted);
                await _mediator.Publish(new BulkDeleteMetricsNotification(jobInfo.Id, itemsDeleted), cancellationToken);

                if (result.Issues.Count > 0)
                {
                    var exception = new JobExecutionException($"Exception encounted while deleting resources: {result.Issues[0].Message}", result);
                    exception.RequestCancellationOnFailure = true;
                    throw exception;
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
