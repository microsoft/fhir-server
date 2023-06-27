// -------------------------------------------------------------------------------------------------
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
        private readonly Func<IScoped<IDeleter>> _deleterFactory;
        private readonly RequestContextAccessor<IFhirRequestContext> _contextAccessor;
        private readonly IMediator _mediator;

        public BulkDeleteProcessingJob(
            Func<IScoped<IDeleter>> deleterFactory,
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

                using IScoped<IDeleter> deleter = _deleterFactory.Invoke();
                var itemsDeleted = await deleter.Value.DeleteMultipleAsync(new ConditionalDeleteResourceRequest(definition.Type, (IReadOnlyList<Tuple<string, string>>)definition.SearchParameters, definition.DeleteOperation, -1), cancellationToken);

                var result = new BulkDeleteResult();
                result.ResourcesDeleted.Add(definition.Type, itemsDeleted);

                await _mediator.Publish(new BulkDeleteMetricsNotification(jobInfo.Id, itemsDeleted), cancellationToken);

                return JsonConvert.SerializeObject(result);
            }
            finally
            {
                _contextAccessor.RequestContext = existingFhirRequestContext;
            }
        }
    }
}
