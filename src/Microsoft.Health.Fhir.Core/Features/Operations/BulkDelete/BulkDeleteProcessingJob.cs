﻿// -------------------------------------------------------------------------------------------------
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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Operations.BulkDelete.Messages;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Messages.Delete;
using Microsoft.Health.Fhir.Core.Models;
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
        private readonly Func<IScoped<ISearchService>> _searchService;
        private readonly IQueueClient _queueClient;
        private readonly IModelInfoProvider _modelInfoProvider;
        private readonly ILogger<BulkDeleteProcessingJob> _logger;

        public BulkDeleteProcessingJob(
            Func<IScoped<IDeletionService>> deleterFactory,
            RequestContextAccessor<IFhirRequestContext> contextAccessor,
            IMediator mediator,
            Func<IScoped<ISearchService>> searchService,
            IQueueClient queueClient,
            IModelInfoProvider modelInfoProvider,
            ILogger<BulkDeleteProcessingJob> logger)
        {
            _deleterFactory = EnsureArg.IsNotNull(deleterFactory, nameof(deleterFactory));
            _contextAccessor = EnsureArg.IsNotNull(contextAccessor, nameof(contextAccessor));
            _mediator = EnsureArg.IsNotNull(mediator, nameof(mediator));
            _searchService = EnsureArg.IsNotNull(searchService, nameof(searchService));
            _queueClient = EnsureArg.IsNotNull(queueClient, nameof(queueClient));
            _modelInfoProvider = EnsureArg.IsNotNull(modelInfoProvider, nameof(modelInfoProvider));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
        }

        public async Task<string> ExecuteAsync(JobInfo jobInfo, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(jobInfo, nameof(jobInfo));

            IFhirRequestContext existingFhirRequestContext = _contextAccessor.RequestContext;

            try
            {
                BulkDeleteDefinition definition = jobInfo.DeserializeDefinition<BulkDeleteDefinition>();

                Activity.Current?.SetParentId(definition.ParentRequestId);

                var fhirRequestContext = new FhirRequestContext(
                    method: "BulkDelete",
                    uriString: definition.Url,
                    baseUriString: definition.BaseUrl,
                    correlationId: jobInfo.Id.ToString() + '-' + jobInfo.GroupId)
                {
                    IsBackgroundTask = true,
                };

                _contextAccessor.RequestContext = fhirRequestContext;
                var result = new BulkDeleteResult();
                IDictionary<string, long> resourcesDeleted = new Dictionary<string, long>();
                using IScoped<IDeletionService> deleter = _deleterFactory.Invoke();
                Exception exception = null;
                List<string> types = definition.Type.SplitByOrSeparator().ToList();
                List<ResourceWrapper> deletedSearchParameters = new List<ResourceWrapper>();

                try
                {
                    resourcesDeleted = await deleter.Value.DeleteMultipleAsync(
                        new ConditionalDeleteResourceRequest(
                            types[0],
                            (IReadOnlyList<Tuple<string, string>>)definition.SearchParameters,
                            definition.DeleteOperation,
                            maxDeleteCount: null,
                            deleteAll: true,
                            versionType: definition.VersionType,
                            allowPartialSuccess: false), // Explicitly setting to call out that this can be changed in the future if we want to. Bulk delete offers the possibility of automatically rerunning the operation until it succeeds, fully automating the process.
                        cancellationToken,
                        definition.ExcludedResourceTypes,
                        deletedSearchParameters);
                }
                catch (IncompleteOperationException<IDictionary<string, long>> ex)
                {
                    _logger.LogError(ex, "Deleting resources failed.");
                    resourcesDeleted = ex.PartialResults;
                    result.Issues.Add(ex.Message);
                    exception = ex;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Deleting resources failed.");
                    throw;
                }

                foreach (var (key, value) in resourcesDeleted)
                {
                    if (!result.ResourcesDeleted.TryAdd(key, value))
                    {
                        result.ResourcesDeleted[key] += value;
                    }
                }

                try
                {
                    var notification = new BulkDeleteMetricsNotification(jobInfo.Id, resourcesDeleted.Count)
                    {
                        Content = CreateNotificationContent(deletedSearchParameters),
                    };

                    await _mediator.Publish(notification, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to create and publish the notification.");
                    throw;
                }

                if (exception != null)
                {
                    throw new JobExecutionException($"Exception encounted while deleting resources: {result.Issues.First()}", result, exception, false);
                }

                if (types.Count > 1)
                {
                    types.RemoveAt(0);
                    using var searchService = _searchService.Invoke();
                    BulkDeleteDefinition processingDefinition = await BulkDeleteOrchestratorJob.CreateProcessingDefinition(definition, searchService.Value, types, cancellationToken);

                    if (processingDefinition != null)
                    {
                        await _queueClient.EnqueueAsync(QueueType.BulkDelete, cancellationToken, jobInfo.GroupId, definitions: processingDefinition);
                    }
                }

                return JsonConvert.SerializeObject(result);
            }
            finally
            {
                _contextAccessor.RequestContext = existingFhirRequestContext;
            }
        }

        private string CreateNotificationContent(List<ResourceWrapper> resources)
        {
            try
            {
                var searchParameterUrls = resources
                    .Where(x => string.Equals(x.ResourceTypeName, KnownResourceTypes.SearchParameter, StringComparison.OrdinalIgnoreCase) && x.RawResource != null)
                    .Select(x =>
                    {
                        try
                        {
                            return _modelInfoProvider.ToTypedElement(x.RawResource).GetStringScalar("url");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"Failed to extract the url from the resource, '{x.ResourceId}'.");
                            return null;
                        }
                    })
                    .Where(x => x != null)
                    .ToList();
                _logger.LogInformation($"Creating the notification content with {searchParameterUrls.Count} search parameters.");
                if (searchParameterUrls.Any())
                {
                    return JsonConvert.SerializeObject(searchParameterUrls);
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to create the notification content for {resources.Count} search parameters.");
                throw;
            }
        }
    }
}
