// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.Serialization;
using Hl7.Fhir.Support;
using Medino;
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
using Microsoft.Health.Fhir.Core.Features.Validation;
using Microsoft.Health.Fhir.Core.Messages.Patch;
using Microsoft.Health.Fhir.Core.Messages.Upsert;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.JobManagement;
using Newtonsoft.Json;
using Polly;
using Polly.Retry;

namespace Microsoft.Health.Fhir.Core.Features.Operations.BulkUpdate
{
    /// <summary>
    /// Executes bulk update processing for FHIR resources within a bulk update job group.
    /// Reads each processing job, prepares query parameters, and coordinates updates using the bulk update service.
    /// Handles error reporting, publishes update metrics, and manages request context propagation.
    /// Refreshes supported FHIR profiles if profile resources are updated.
    /// Implements robust exception handling to distinguish between partial and complete failures.
    /// </summary>
    [JobTypeId((int)JobType.BulkUpdateProcessing)]
    public class BulkUpdateProcessingJob : IJob
    {
        private readonly IQueueClient _queueClient;
        private readonly RequestContextAccessor<IFhirRequestContext> _contextAccessor;
        private readonly Func<IScoped<IBulkUpdateService>> _updateFactory;
        private readonly IMediator _mediator;
        private readonly ISupportedProfilesStore _supportedProfiles;
        private readonly ILogger<BulkUpdateProcessingJob> _logger;
        internal const uint ProcessingBatchSize = 1000;

        public BulkUpdateProcessingJob(
            IQueueClient queueClient,
            Func<IScoped<IBulkUpdateService>> updateFactory,
            RequestContextAccessor<IFhirRequestContext> contextAccessor,
            ISupportedProfilesStore supportedProfiles,
            IMediator mediator,
            ILogger<BulkUpdateProcessingJob> logger)
        {
            _queueClient = EnsureArg.IsNotNull(queueClient, nameof(queueClient));
            _updateFactory = EnsureArg.IsNotNull(updateFactory, nameof(updateFactory));
            _contextAccessor = EnsureArg.IsNotNull(contextAccessor, nameof(contextAccessor));
            _supportedProfiles = EnsureArg.IsNotNull(supportedProfiles, nameof(supportedProfiles));
            _mediator = EnsureArg.IsNotNull(mediator, nameof(mediator));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
        }

        public async Task<string> ExecuteAsync(JobInfo jobInfo, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(jobInfo, nameof(jobInfo));

            IFhirRequestContext existingFhirRequestContext = _contextAccessor.RequestContext;
            var result = new BulkUpdateResult();

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
                using IScoped<IBulkUpdateService> upsertService = _updateFactory.Invoke();
                Exception exception = null;
                uint readUpto = 0;

                var queryParametersList = new List<Tuple<string, string>>();
                if (definition.SearchParameters is not null)
                {
                    queryParametersList.AddRange(definition.SearchParameters);
                }

                if (definition.GlobalEndSurrogateId != null) // no need to check individually as they all should have values if anyone does
                {
                    queryParametersList.Add(Tuple.Create(KnownQueryParameterNames.Type, definition.Type));
                    queryParametersList.Add(Tuple.Create(KnownQueryParameterNames.GlobalEndSurrogateId, definition.GlobalEndSurrogateId));
                    queryParametersList.Add(Tuple.Create(KnownQueryParameterNames.EndSurrogateId, definition.EndSurrogateId));
                    queryParametersList.Add(Tuple.Create(KnownQueryParameterNames.GlobalStartSurrogateId, definition.GlobalStartSurrogateId));
                    queryParametersList.Add(Tuple.Create(KnownQueryParameterNames.StartSurrogateId, definition.StartSurrogateId));

                    // Subjobs based on resource type-surrogate id ranges, are already scoped to a range definition.MaximumNumberOfResourcesPerQuery
                    queryParametersList.Add(Tuple.Create(KnownQueryParameterNames.Count, definition.MaximumNumberOfResourcesPerQuery.ToString()));
                }
                else
                {
                    // We want to process everything else in the batches of 1000
                    queryParametersList.Add(Tuple.Create(KnownQueryParameterNames.Count, definition.MaximumNumberOfResourcesPerQuery <= ProcessingBatchSize ? definition.MaximumNumberOfResourcesPerQuery.ToString() : ProcessingBatchSize.ToString()));
                }

                try
                {
                    // If readNextPage is true, we want to read all the pages until there are no more results and we ignore the readUpto value
                    // If readNextPage is false, we want to read only one page when readUpto is 1 or 0. OR read until the readUpto value

                    // For serial executions, readNextPage is true and it should read next pages hence readUpto is 0
                    // For subjobs based on resource type-surrogate id ranges, readNextPage is false since they are already scoped to a range hence readUpto is 0
                    // For CT level jobs, readNextPage is false and readUpto is set based on 1k batches
                    readUpto = (definition.ReadNextPage || definition.GlobalEndSurrogateId != null)
                                ? 0
                                : (definition.MaximumNumberOfResourcesPerQuery <= 1000
                                      ? 1
                                      : ((definition.MaximumNumberOfResourcesPerQuery - 1) / 1000) + 1);

                    result = await upsertService.Value.UpdateMultipleAsync(definition.Type, definition.Parameters, definition.ReadNextPage, readUpto, isIncludesRequest: false, queryParametersList, null, cancellationToken);
                }
                catch (IncompleteOperationException<BulkUpdateResult> ex)
                {
                    result = ex.PartialResults;
                    result.Issues.Add(ex.Message);
                    exception = ex;
                }

                if (result.ResourcesUpdated.Any())
                {
                    await _mediator.PublishAsync(new BulkUpdateMetricsNotification(jobInfo.Id, result.ResourcesUpdated.Sum(resource => resource.Value)), cancellationToken);
                }

                if (exception != null)
                {
                    throw new JobExecutionException($"Exception encounted while updating resources: {result.Issues.First()}", result, exception, false);
                }
                else
                {
                    if (result.ResourcesPatchFailed.Any())
                    {
                        _logger.LogWarning("Bulk update job {GroupId} and {JobId} completed with {Count} resources updated, but {FailedToPatchCount} resources failed to patch.", jobInfo.GroupId, jobInfo.Id, result.ResourcesUpdated.Sum(resource => resource.Value), result.ResourcesPatchFailed.Sum(resource => resource.Value));
                        throw new JobExecutionSoftFailureException($"Exception encounted while updating resources", result, true);
                    }
                    else
                    {
                        _logger.LogInformation("Bulk update job {GroupId} and {JobId} completed successfully with {Count} resources updated.", jobInfo.GroupId, jobInfo.Id, result.ResourcesUpdated.Sum(resource => resource.Value));
                    }
                }

                return JsonConvert.SerializeObject(result);
            }
            finally
            {
                _contextAccessor.RequestContext = existingFhirRequestContext;

                // Get all jobs for the group
                var jobs = (await _queueClient.GetJobByGroupIdAsync(QueueType.BulkUpdate, jobInfo.GroupId, true, cancellationToken)).ToList();

                // Filter out the current job and group job, and keep only active subjobs
                var activeJobs = jobs.Where(j => j.Id != jobInfo.Id && j.Id != jobInfo.GroupId &&
                    (j.Status == JobStatus.Created || j.Status == JobStatus.Running)).ToList();

                // Only proceed if this is the last active subjob in the group
                if (activeJobs.Count == 0)
                {
                    _logger.LogInformation("This is the last processing job of Bulk update group job {GroupId}. Checking if any profile resources were updated", jobInfo.GroupId);
                    var profileTypes = _supportedProfiles.GetProfilesTypes();

                    // Check if current result or any completed job in the group updated a profile resource type
                    bool needRefresh = result.ResourcesUpdated.Keys.Any(profileTypes.Contains);

                    // Filter jobs to completed subjobs with results (excluding group job)
                    var completedJobs = jobs.Where(j => j.Id != jobInfo.GroupId && j.Result != null).ToList();

                    if (!needRefresh)
                    {
                        foreach (var job in completedJobs)
                        {
                            BulkUpdateResult bulkUpdateResult;
                            try
                            {
                                bulkUpdateResult = job.DeserializeResult<BulkUpdateResult>();
                            }
                            catch
                            {
                                continue; // Skip if deserialization fails
                            }

                            if (bulkUpdateResult?.ResourcesUpdated.Keys.Any(profileTypes.Contains) == true)
                            {
                                _supportedProfiles.Refresh();
                                _logger.LogInformation("Bulk update job {GroupId} and {JobId} updated profile resources, refreshing supported profiles.", job.GroupId, job.Id);
                                break;
                            }
                        }
                    }
                    else
                    {
                        _logger.LogInformation("Bulk update job {GroupId} and {JobId} updated profile resources, refreshing supported profiles.", jobInfo.GroupId, jobInfo.Id);
                        _supportedProfiles.Refresh();
                    }
                }
            }
        }
    }
}
