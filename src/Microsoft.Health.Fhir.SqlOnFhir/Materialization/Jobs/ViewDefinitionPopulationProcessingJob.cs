// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.ViewDefinitionRun;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlOnFhir.Channels;
using Microsoft.Health.JobManagement;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.SqlOnFhir.Materialization.Jobs;

/// <summary>
/// Processing job for populating a materialized ViewDefinition table.
/// Searches for resources in batches using continuation tokens and materializes
/// each resource's ViewDefinition rows into the target(s) determined by the registration.
/// Enqueues follow-up jobs when more resources remain.
/// </summary>
[JobTypeId((int)JobType.ViewDefinitionPopulationProcessing)]
public sealed class ViewDefinitionPopulationProcessingJob : IJob
{
    private readonly Func<IScoped<ISearchService>> _searchServiceFactory;
    private readonly IResourceDeserializer _resourceDeserializer;
    private readonly MaterializerFactory _materializerFactory;
    private readonly IViewDefinitionSubscriptionManager _subscriptionManager;
    private readonly IQueueClient _queueClient;
    private readonly IMediator _mediator;
    private readonly ILogger<ViewDefinitionPopulationProcessingJob> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ViewDefinitionPopulationProcessingJob"/> class.
    /// </summary>
    public ViewDefinitionPopulationProcessingJob(
        Func<IScoped<ISearchService>> searchServiceFactory,
        IResourceDeserializer resourceDeserializer,
        MaterializerFactory materializerFactory,
        IViewDefinitionSubscriptionManager subscriptionManager,
        IQueueClient queueClient,
        IMediator mediator,
        ILogger<ViewDefinitionPopulationProcessingJob> logger)
    {
        _searchServiceFactory = searchServiceFactory;
        _resourceDeserializer = resourceDeserializer;
        _materializerFactory = materializerFactory;
        _subscriptionManager = subscriptionManager;
        _queueClient = queueClient;
        _mediator = mediator;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string> ExecuteAsync(JobInfo jobInfo, CancellationToken cancellationToken)
    {
        var definition = jobInfo.DeserializeDefinition<ViewDefinitionPopulationProcessingJobDefinition>();

        // Use the target from the job definition (propagated from orchestrator).
        // The target is always explicitly set — no fallback to a default materializer.
        MaterializationTarget target = definition.Target;

        _logger.LogWarning(
            "[VDPopulate] Processing job started for '{ViewDefName}' (resource type: {ResourceType}, target: {Target}, continuationToken: {HasToken})",
            definition.ViewDefinitionName,
            definition.ResourceType,
            target,
            !string.IsNullOrEmpty(definition.ContinuationToken));

        long totalResourcesProcessed = 0;
        long totalRowsInserted = 0;
        long totalFailedResources = 0;
        string? currentContinuationToken = definition.ContinuationToken;

        using IScoped<ISearchService> searchServiceScope = _searchServiceFactory();
        ISearchService searchService = searchServiceScope.Value;

        // Process resources in batches within this job
        bool hasMoreResults = true;
        int batchesProcessedInThisJob = 0;
        const int maxBatchesPerJob = 10; // Limit per job to allow heartbeats and checkpointing

        while (hasMoreResults && batchesProcessedInThisJob < maxBatchesPerJob && !cancellationToken.IsCancellationRequested)
        {
            // Build search query parameters
            var queryParameters = new List<Tuple<string, string>>
            {
                Tuple.Create("_count", definition.BatchSize.ToString()),
            };

            if (!string.IsNullOrEmpty(currentContinuationToken))
            {
                queryParameters.Add(Tuple.Create(
                    KnownQueryParameterNames.ContinuationToken,
                    ContinuationTokenEncoder.Encode(currentContinuationToken)));
            }

            // Search for resources
            SearchResult searchResult = await searchService.SearchAsync(
                definition.ResourceType,
                queryParameters,
                cancellationToken,
                isAsyncOperation: true);

            var results = searchResult.Results.ToList();

            _logger.LogWarning(
                "[VDPopulate] Batch {BatchNumber}/{MaxBatches} for '{ViewDefName}': found {Count} {ResourceType} resources (cumulative: {TotalProcessed} processed, {TotalRows} rows, {TotalFailed} failed)",
                batchesProcessedInThisJob + 1,
                maxBatchesPerJob,
                definition.ViewDefinitionName,
                results.Count,
                definition.ResourceType,
                totalResourcesProcessed,
                totalRowsInserted,
                totalFailedResources);

            // Deserialize resources and build the batch. Any individual resource that fails to
            // deserialize is skipped and counted as a failure — the rest still get batched together.
            var batch = new List<(ResourceElement Resource, string ResourceKey)>(results.Count);
            foreach (SearchResultEntry entry in results)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                try
                {
                    ResourceElement resourceElement = _resourceDeserializer.Deserialize(entry.Resource);
                    string resourceKey = $"{entry.Resource.ResourceTypeName}/{entry.Resource.ResourceId}";
                    batch.Add((resourceElement, resourceKey));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    totalFailedResources++;
                    _logger.LogWarning(
                        ex,
                        "Failed to deserialize resource {ResourceType}/{ResourceId} for ViewDefinition '{ViewDefName}'",
                        entry.Resource.ResourceTypeName,
                        entry.Resource.ResourceId,
                        definition.ViewDefinitionName);
                }
            }

            if (batch.Count > 0 && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    int rowsInserted = await _materializerFactory.UpsertResourceBatchAsync(
                        target,
                        definition.ViewDefinitionJson,
                        definition.ViewDefinitionName,
                        batch,
                        cancellationToken);

                    totalRowsInserted += rowsInserted;
                    totalResourcesProcessed += batch.Count;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // If the batch fails as a whole, fall back to per-resource upserts to isolate
                    // the failing resource(s) and still make forward progress on the rest.
                    _logger.LogWarning(
                        ex,
                        "[VDPopulate] Batch upsert failed for '{ViewDefName}' ({Count} resources); falling back to per-resource upsert",
                        definition.ViewDefinitionName,
                        batch.Count);

                    foreach ((ResourceElement resourceElement, string resourceKey) in batch)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            break;
                        }

                        try
                        {
                            int rowsInserted = await _materializerFactory.UpsertResourceAsync(
                                target,
                                definition.ViewDefinitionJson,
                                definition.ViewDefinitionName,
                                resourceElement,
                                resourceKey,
                                cancellationToken);
                            totalRowsInserted += rowsInserted;
                            totalResourcesProcessed++;
                        }
                        catch (Exception innerEx) when (innerEx is not OperationCanceledException)
                        {
                            totalFailedResources++;
                            _logger.LogWarning(
                                innerEx,
                                "Failed to materialize resource '{ResourceKey}' for ViewDefinition '{ViewDefName}' (fallback path)",
                                resourceKey,
                                definition.ViewDefinitionName);
                        }
                    }
                }
            }

            // Check for more results
            currentContinuationToken = searchResult.ContinuationToken;
            hasMoreResults = !string.IsNullOrEmpty(currentContinuationToken);
            batchesProcessedInThisJob++;
        }

        // If there are more resources, enqueue a follow-up processing job
        if (hasMoreResults && !cancellationToken.IsCancellationRequested)
        {
            var nextDefinition = new ViewDefinitionPopulationProcessingJobDefinition
            {
                RegistrationId = definition.RegistrationId,
                ViewDefinitionJson = definition.ViewDefinitionJson,
                ViewDefinitionName = definition.ViewDefinitionName,
                ResourceType = definition.ResourceType,
                BatchSize = definition.BatchSize,
                ContinuationToken = currentContinuationToken,
                LibraryResourceId = definition.LibraryResourceId,
                Target = target,
            };

            await _queueClient.EnqueueAsync(
                (byte)QueueType.ViewDefinitionPopulation,
                new[] { JsonConvert.SerializeObject(nextDefinition) },
                jobInfo.GroupId,
                forceOneActiveJobGroup: false,
                cancellationToken);

            _logger.LogWarning(
                "[VDPopulate] Enqueued follow-up processing job for '{ViewDefName}' after {Processed} resources ({Rows} rows); more batches remain",
                definition.ViewDefinitionName,
                totalResourcesProcessed,
                totalRowsInserted);
        }
        else
        {
            // No more resources — population is complete. Notify the subscription manager.
            _logger.LogWarning(
                "[VDPopulate] Publishing ViewDefinitionPopulationCompleteNotification for '{ViewDefName}' " +
                "(success={Success}, totalResources={Resources}, totalRows={Rows}, failures={Failures})",
                definition.ViewDefinitionName,
                totalFailedResources == 0,
                totalResourcesProcessed,
                totalRowsInserted,
                totalFailedResources);

            // Propagate notification failures so the job itself fails, letting the JobQueue framework
            // retry deterministically. We rely on the job framework as the source of truth for "done"
            // — we never infer readiness. The last batch is idempotent (DELETE+MERGE for Delta, upsert
            // for SQL), so re-processing on retry is safe.
            await _mediator.Publish(
                new ViewDefinitionPopulationCompleteNotification(
                    definition.ViewDefinitionName,
                    success: totalFailedResources == 0,
                    rowsInserted: totalRowsInserted,
                    errorMessage: totalFailedResources > 0 ? $"{totalFailedResources} resources failed" : null,
                    libraryResourceId: definition.LibraryResourceId),
                cancellationToken);

            _logger.LogInformation(
                "ViewDefinitionPopulationCompleteNotification published for '{ViewDefName}'",
                definition.ViewDefinitionName);
        }

        var result = new ViewDefinitionPopulationProcessingJobResult
        {
            ResourcesProcessed = totalResourcesProcessed,
            RowsInserted = totalRowsInserted,
            FailedResources = totalFailedResources,
            NextContinuationToken = hasMoreResults ? currentContinuationToken : null,
        };

        _logger.LogInformation(
            "ViewDefinition population processing completed for '{ViewDefName}': " +
            "{ResourcesProcessed} resources processed, {RowsInserted} rows inserted, {FailedResources} failures",
            definition.ViewDefinitionName,
            result.ResourcesProcessed,
            result.RowsInserted,
            result.FailedResources);

        return JsonConvert.SerializeObject(result);
    }
}
