// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.Serialization;
using Hl7.Fhir.Support;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Operations.BulkUpdate;
using Microsoft.Health.Fhir.Core.Features.Operations.BulkUpdate.Messages;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Messages.Patch;
using Microsoft.Health.Fhir.Core.Messages.Upsert;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.JobManagement;
using Newtonsoft.Json;
using Polly;
using Polly.Retry;

namespace Microsoft.Health.Fhir.Core.Features.Operations.BulkUpdate
{
    [JobTypeId((int)JobType.BulkUpdateProcessing)]
    public class BulkUpdateProcessingJob : IJob
    {
        private readonly IQueueClient _queueClient;
        private readonly RequestContextAccessor<IFhirRequestContext> _contextAccessor;
        private readonly Func<IScoped<IBulkUpdateService>> _updateFactory;
        private readonly IMediator _mediator;
        private readonly ILogger<BulkUpdateProcessingJob> _logger;

        public BulkUpdateProcessingJob(
            IQueueClient queueClient,
            Func<IScoped<IBulkUpdateService>> updateFactory,
            RequestContextAccessor<IFhirRequestContext> contextAccessor,
            ILogger<BulkUpdateProcessingJob> logger,
            IMediator mediator)
        {
            _queueClient = EnsureArg.IsNotNull(queueClient, nameof(queueClient));
            _updateFactory = EnsureArg.IsNotNull(updateFactory, nameof(updateFactory));
            _contextAccessor = EnsureArg.IsNotNull(contextAccessor, nameof(contextAccessor));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
            _mediator = EnsureArg.IsNotNull(mediator, nameof(mediator));
        }

        public async Task<string> ExecuteAsync(JobInfo jobInfo, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(jobInfo, nameof(jobInfo));

            IFhirRequestContext existingFhirRequestContext = _contextAccessor.RequestContext;

            try
            {
                BulkUpdateDefinition definition = jobInfo.DeserializeDefinition<BulkUpdateDefinition>();

                Activity.Current?.SetParentId(definition.ParentRequestId);

                var fhirRequestContext = new FhirRequestContext(
                    method: "BulkUpdate",
                    uriString: definition.Url,
                    baseUriString: definition.BaseUrl,
                    correlationId: jobInfo.Id.ToString() + '-' + jobInfo.GroupId.ToString(),
                    requestHeaders: new Dictionary<string, StringValues>(),
                    responseHeaders: new Dictionary<string, StringValues>())
                {
                    IsBackgroundTask = true,
                };

                _contextAccessor.RequestContext = fhirRequestContext;
                var result = new BulkUpdateResult();
                IDictionary<string, long> resourcesUpdated = new Dictionary<string, long>();
                using IScoped<IBulkUpdateService> upsertService = _updateFactory.Invoke();
                Exception exception = null;

                var tillTime = new PartialDateTime(jobInfo.CreateDate);
                var queryParametersList = new List<Tuple<string, string>>();
                queryParametersList.AddRange(definition.SearchParameters);

                queryParametersList.Add(Tuple.Create(KnownQueryParameterNames.Count, definition.MaximumNumberOfResourcesPerQuery.ToString(CultureInfo.InvariantCulture)));
                queryParametersList.Add(Tuple.Create(KnownQueryParameterNames.LastUpdated, $"le{tillTime}"));

                if (definition.GlobalEndSurrogateId != null) // no need to check individually as they all should have values if anyone does
                {
                    queryParametersList.Add(Tuple.Create(KnownQueryParameterNames.Type, definition.Type));
                    queryParametersList.Add(Tuple.Create(KnownQueryParameterNames.GlobalEndSurrogateId, definition.GlobalEndSurrogateId));
                    queryParametersList.Add(Tuple.Create(KnownQueryParameterNames.EndSurrogateId, definition.EndSurrogateId));
                    queryParametersList.Add(Tuple.Create(KnownQueryParameterNames.GlobalStartSurrogateId, definition.GlobalStartSurrogateId));
                    queryParametersList.Add(Tuple.Create(KnownQueryParameterNames.StartSurrogateId, definition.StartSurrogateId));
                }

                try
                {
                    bool isIncludesOperation = definition.SearchParameters.Any(p => p.Item1.Equals(KnownQueryParameterNames.IncludesContinuationToken, StringComparison.OrdinalIgnoreCase));
                    result = await upsertService.Value.UpdateMultipleAsync(definition.Type, definition.Parameters, definition.ReadNextPage, definition.MaximumNumberOfResourcesPerQuery, isIncludesOperation, queryParametersList, null, cancellationToken);
                    resourcesUpdated = result.ResourcesUpdated;
                }
                catch (IncompleteOperationException<BulkUpdateResult> ex)
                {
                    resourcesUpdated = ex.PartialResults.ResourcesUpdated;
                    result = ex.PartialResults;
                    result.Issues.Add(ex.Message);
                    exception = ex;
                }

                await _mediator.Publish(new BulkUpdateMetricsNotification(jobInfo.Id, resourcesUpdated.Sum(resource => resource.Value)), cancellationToken);

                if (exception != null)
                {
                    throw new JobExecutionException($"Exception encounted while updating resources: {result.Issues.First()}", result, exception, false);
                }
                else
                {
                    if (result.ResourcesPatchFailed.Any())
                    {
                        _logger.LogWarning("Bulk update job {GroupId} and {JobId} completed with {Count} resources updated, but {FailedToPatchCount} resources failed to patch.", jobInfo.GroupId, jobInfo.Id, resourcesUpdated.Sum(resource => resource.Value), result.ResourcesPatchFailed.Sum(resource => resource.Value));
                        throw new JobExecutionSoftFailureException($"Exception encounted while updating resources", result, true);
                    }
                    else
                    {
                        _logger.LogInformation("Bulk update job {GroupId} and {JobId} completed successfully with {Count} resources updated.", jobInfo.GroupId, jobInfo.Id, resourcesUpdated.Sum(resource => resource.Value));
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
