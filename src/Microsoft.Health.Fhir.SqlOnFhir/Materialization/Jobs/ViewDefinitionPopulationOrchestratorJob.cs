// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.JobManagement;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.SqlOnFhir.Materialization.Jobs;

/// <summary>
/// Orchestrator job for fully populating a materialized ViewDefinition table.
/// Creates the SQL table (if needed) and enqueues a processing job to iterate all
/// resources of the target type.
/// </summary>
[JobTypeId((int)JobType.ViewDefinitionPopulationOrchestrator)]
public sealed class ViewDefinitionPopulationOrchestratorJob : IJob
{
    private readonly IViewDefinitionSchemaManager _schemaManager;
    private readonly IQueueClient _queueClient;
    private readonly ILogger<ViewDefinitionPopulationOrchestratorJob> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ViewDefinitionPopulationOrchestratorJob"/> class.
    /// </summary>
    /// <param name="schemaManager">The schema manager for creating materialized tables.</param>
    /// <param name="queueClient">The queue client for enqueuing child processing jobs.</param>
    /// <param name="logger">The logger instance.</param>
    public ViewDefinitionPopulationOrchestratorJob(
        IViewDefinitionSchemaManager schemaManager,
        IQueueClient queueClient,
        ILogger<ViewDefinitionPopulationOrchestratorJob> logger)
    {
        _schemaManager = schemaManager;
        _queueClient = queueClient;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string> ExecuteAsync(JobInfo jobInfo, CancellationToken cancellationToken)
    {
        var definition = jobInfo.DeserializeDefinition<ViewDefinitionPopulationOrchestratorJobDefinition>();

        _logger.LogInformation(
            "Starting ViewDefinition population orchestrator for '{ViewDefName}' targeting '{ResourceType}' (materialization target: {Target})",
            definition.ViewDefinitionName,
            definition.ResourceType,
            definition.Target);

        bool tableCreated = false;

        // Only create a SQL table when the target includes SqlServer.
        // Fabric (Delta Lake) and Parquet targets create their own storage structures during materialization.
        if (definition.Target.HasFlag(MaterializationTarget.SqlServer))
        {
            bool tableExists = await _schemaManager.TableExistsAsync(definition.ViewDefinitionName, cancellationToken);

            if (!tableExists)
            {
                string qualifiedTable = await _schemaManager.CreateTableAsync(definition.ViewDefinitionJson, cancellationToken);
                _logger.LogInformation("Created materialized SQL table '{TableName}'", qualifiedTable);
                tableCreated = true;
            }
            else
            {
                _logger.LogInformation(
                    "Materialized SQL table for '{ViewDefName}' already exists, proceeding with population",
                    definition.ViewDefinitionName);
            }
        }
        else
        {
            _logger.LogInformation(
                "Target '{Target}' does not include SqlServer — skipping SQL table creation for '{ViewDefName}'",
                definition.Target,
                definition.ViewDefinitionName);
        }

        // Step 2: Enqueue the initial processing job (starts with no continuation token)
        var processingDefinition = new ViewDefinitionPopulationProcessingJobDefinition
        {
            RegistrationId = definition.RegistrationId,
            ViewDefinitionJson = definition.ViewDefinitionJson,
            ViewDefinitionName = definition.ViewDefinitionName,
            ResourceType = definition.ResourceType,
            BatchSize = definition.BatchSize,
            ContinuationToken = null,
            LibraryResourceId = definition.LibraryResourceId,
        };

        string serializedDefinition = JsonConvert.SerializeObject(processingDefinition);

        var enqueuedJobs = await _queueClient.EnqueueAsync(
            (byte)QueueType.ViewDefinitionPopulation,
            new[] { serializedDefinition },
            jobInfo.GroupId,
            forceOneActiveJobGroup: false,
            cancellationToken);

        _logger.LogInformation(
            "Enqueued {JobCount} processing job(s) for ViewDefinition '{ViewDefName}'",
            enqueuedJobs.Count,
            definition.ViewDefinitionName);

        var result = new
        {
            ViewDefinitionName = definition.ViewDefinitionName,
            ResourceType = definition.ResourceType,
            Target = definition.Target.ToString(),
            TableCreated = tableCreated,
            ProcessingJobsEnqueued = enqueuedJobs.Count,
        };

        return JsonConvert.SerializeObject(result);
    }
}
