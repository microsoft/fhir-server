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
using Microsoft.Health.JobManagement;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.SqlOnFhir.Materialization.Jobs;

/// <summary>
/// Processing job for populating a materialized ViewDefinition table.
/// Searches for resources in batches using continuation tokens and materializes
/// each resource's ViewDefinition rows into the SQL table. Enqueues follow-up jobs
/// when more resources remain.
/// </summary>
[JobTypeId((int)JobType.ViewDefinitionPopulationProcessing)]
public sealed class ViewDefinitionPopulationProcessingJob : IJob
{
    private readonly Func<IScoped<ISearchService>> _searchServiceFactory;
    private readonly IResourceDeserializer _resourceDeserializer;
    private readonly IViewDefinitionMaterializer _materializer;
    private readonly IQueueClient _queueClient;
    private readonly IMediator _mediator;
    private readonly ILogger<ViewDefinitionPopulationProcessingJob> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ViewDefinitionPopulationProcessingJob"/> class.
    /// </summary>
    public ViewDefinitionPopulationProcessingJob(
        Func<IScoped<ISearchService>> searchServiceFactory,
        IResourceDeserializer resourceDeserializer,
        IViewDefinitionMaterializer materializer,
        IQueueClient queueClient,
        IMediator mediator,
        ILogger<ViewDefinitionPopulationProcessingJob> logger)
    {
        _searchServiceFactory = searchServiceFactory;
        _resourceDeserializer = resourceDeserializer;
        _materializer = materializer;
        _queueClient = queueClient;
        _mediator = mediator;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string> ExecuteAsync(JobInfo jobInfo, CancellationToken cancellationToken)
    {
        var definition = jobInfo.DeserializeDefinition<ViewDefinitionPopulationProcessingJobDefinition>();

        _logger.LogInformation(
            "Starting ViewDefinition population processing for '{ViewDefName}' (resource type: {ResourceType})",
            definition.ViewDefinitionName,
            definition.ResourceType);

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

            _logger.LogInformation(
                "Batch {BatchNumber}: Found {Count} {ResourceType} resources to materialize for '{ViewDefName}'",
                batchesProcessedInThisJob + 1,
                results.Count,
                definition.ResourceType,
                definition.ViewDefinitionName);

            // Materialize each resource
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

                    int rowsInserted = await _materializer.UpsertResourceAsync(
                        definition.ViewDefinitionJson,
                        definition.ViewDefinitionName,
                        resourceElement,
                        resourceKey,
                        cancellationToken);

                    totalRowsInserted += rowsInserted;
                    totalResourcesProcessed++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    totalFailedResources++;
                    _logger.LogWarning(
                        ex,
                        "Failed to materialize resource {ResourceType}/{ResourceId} for ViewDefinition '{ViewDefName}'",
                        entry.Resource.ResourceTypeName,
                        entry.Resource.ResourceId,
                        definition.ViewDefinitionName);
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
            };

            await _queueClient.EnqueueAsync(
                (byte)QueueType.ViewDefinitionPopulation,
                new[] { JsonConvert.SerializeObject(nextDefinition) },
                jobInfo.GroupId,
                forceOneActiveJobGroup: false,
                cancellationToken);

            _logger.LogInformation(
                "Enqueued follow-up processing job for '{ViewDefName}' with continuation token",
                definition.ViewDefinitionName);
        }
        else
        {
            // No more resources — population is complete. Notify the subscription manager.
            _logger.LogInformation(
                "Publishing ViewDefinitionPopulationCompleteNotification for '{ViewDefName}' " +
                "(success={Success}, rows={Rows})",
                definition.ViewDefinitionName,
                totalFailedResources == 0,
                totalRowsInserted);

            try
            {
                await _mediator.Publish(
                    new ViewDefinitionPopulationCompleteNotification(
                        definition.ViewDefinitionName,
                        success: totalFailedResources == 0,
                        rowsInserted: totalRowsInserted,
                        errorMessage: totalFailedResources > 0 ? $"{totalFailedResources} resources failed" : null),
                    cancellationToken);

                _logger.LogInformation(
                    "ViewDefinitionPopulationCompleteNotification published for '{ViewDefName}'",
                    definition.ViewDefinitionName);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(
                    ex,
                    "Failed to publish ViewDefinitionPopulationCompleteNotification for '{ViewDefName}'",
                    definition.ViewDefinitionName);
            }
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
