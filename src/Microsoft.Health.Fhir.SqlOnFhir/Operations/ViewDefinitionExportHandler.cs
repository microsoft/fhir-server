// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.ViewDefinitionRun;
using Microsoft.Health.Fhir.SqlOnFhir.Channels;
using Microsoft.Health.Fhir.SqlOnFhir.Materialization;
using Microsoft.Health.Fhir.SqlOnFhir.Materialization.Jobs;
using Microsoft.Health.JobManagement;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.SqlOnFhir.Operations;

/// <summary>
/// Handles the $viewdefinition-export operation with two paths:
/// <list type="bullet">
///   <item><b>Fast path</b>: If the ViewDefinition is already materialized in the requested format
///   and destination, returns download URLs immediately.</item>
///   <item><b>Async path</b>: Enqueues a population job to evaluate and export, returns 202 with status URL.</item>
/// </list>
/// </summary>
public sealed class ViewDefinitionExportHandler : IRequestHandler<ViewDefinitionExportRequest, ViewDefinitionExportResponse>
{
    private readonly IViewDefinitionSubscriptionManager _subscriptionManager;
    private readonly IViewDefinitionSchemaManager _schemaManager;
    private readonly IQueueClient _queueClient;
    private readonly SqlOnFhirMaterializationConfiguration _config;
    private readonly ILogger<ViewDefinitionExportHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ViewDefinitionExportHandler"/> class.
    /// </summary>
    public ViewDefinitionExportHandler(
        IViewDefinitionSubscriptionManager subscriptionManager,
        IViewDefinitionSchemaManager schemaManager,
        IQueueClient queueClient,
        IOptions<SqlOnFhirMaterializationConfiguration> config,
        ILogger<ViewDefinitionExportHandler> logger)
    {
        _subscriptionManager = subscriptionManager;
        _schemaManager = schemaManager;
        _queueClient = queueClient;
        _config = config.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ViewDefinitionExportResponse> Handle(
        ViewDefinitionExportRequest request,
        CancellationToken cancellationToken)
    {
        string viewDefName = ResolveViewDefinitionName(request);
        string requestedFormat = (request.Format ?? "ndjson").ToLowerInvariant();

        // Fast path: check if this ViewDefinition is already materialized in a compatible format
        ViewDefinitionRegistration? registration = _subscriptionManager.GetRegistration(viewDefName);

        if (registration != null && IsAlreadyMaterialized(registration, requestedFormat))
        {
            _logger.LogInformation(
                "$viewdefinition-export fast path: '{ViewDefName}' already materialized as {Format}",
                viewDefName,
                requestedFormat);

            return BuildFastPathResponse(registration, requestedFormat);
        }

        // Async path: enqueue a population/export job
        _logger.LogInformation(
            "$viewdefinition-export async path: enqueuing export job for '{ViewDefName}' as {Format}",
            viewDefName,
            requestedFormat);

        string viewDefJson = ResolveViewDefinitionJson(request, registration);
        string resourceType = ExtractResourceType(viewDefJson);

        var jobDefinition = new ViewDefinitionPopulationOrchestratorJobDefinition
        {
            ViewDefinitionJson = viewDefJson,
            ViewDefinitionName = viewDefName,
            ResourceType = resourceType,
            BatchSize = 100,
        };

        var jobs = await _queueClient.EnqueueAsync(
            (byte)QueueType.ViewDefinitionPopulation,
            new[] { JsonConvert.SerializeObject(jobDefinition) },
            groupId: null,
            forceOneActiveJobGroup: true,
            cancellationToken);

        string exportId = jobs.Count > 0 ? jobs[0].Id.ToString(CultureInfo.InvariantCulture) : Guid.NewGuid().ToString();

        return new ViewDefinitionExportResponse(
            isComplete: false,
            exportId: exportId,
            statusUrl: $"Operations/viewdefinition-export/{exportId}");
    }

    private static string ResolveViewDefinitionName(ViewDefinitionExportRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.ViewDefinitionName))
        {
            return request.ViewDefinitionName;
        }

        if (!string.IsNullOrWhiteSpace(request.ViewDefinitionJson))
        {
            return ExtractName(request.ViewDefinitionJson);
        }

        throw new InvalidOperationException("Either viewDefinitionJson or viewDefinitionName is required.");
    }

    private static string ResolveViewDefinitionJson(ViewDefinitionExportRequest request, ViewDefinitionRegistration? registration)
    {
        if (!string.IsNullOrWhiteSpace(request.ViewDefinitionJson))
        {
            return request.ViewDefinitionJson;
        }

        if (registration != null)
        {
            return registration.ViewDefinitionJson;
        }

        throw new InvalidOperationException("ViewDefinition JSON not available for async export.");
    }

    private bool IsAlreadyMaterialized(ViewDefinitionRegistration registration, string requestedFormat)
    {
        // Parquet target is already materialized to configured storage
        if (requestedFormat == "parquet" && registration.Target.HasFlag(MaterializationTarget.Parquet))
        {
            return _config.IsStorageConfigured;
        }

        // SQL-materialized data can serve any format (we read from table and convert)
        if (registration.Target.HasFlag(MaterializationTarget.SqlServer))
        {
            return true;
        }

        return false;
    }

    private ViewDefinitionExportResponse BuildFastPathResponse(ViewDefinitionRegistration registration, string requestedFormat)
    {
        string location;

        if (requestedFormat == "parquet" && registration.Target.HasFlag(MaterializationTarget.Parquet))
        {
            // Point to the Parquet files in configured storage
            string storageBase = _config.StorageAccountUri ?? "(configured-storage)";
            location = $"{storageBase}/{_config.DefaultContainer}/{registration.ViewDefinitionName}/";
        }
        else
        {
            // Point to the $run endpoint which can serve any format from the materialized table
            location = $"ViewDefinition/{registration.ViewDefinitionName}/$run?_format={requestedFormat}";
        }

        var output = new ViewDefinitionExportOutput(
            registration.ViewDefinitionName,
            location,
            requestedFormat);

        return new ViewDefinitionExportResponse(
            isComplete: true,
            outputs: new[] { output });
    }

    private static string ExtractResourceType(string viewDefinitionJson)
    {
        using var doc = JsonDocument.Parse(viewDefinitionJson);
        return doc.RootElement.TryGetProperty("resource", out var resEl)
            ? resEl.GetString() ?? "Resource"
            : "Resource";
    }

    private static string ExtractName(string viewDefinitionJson)
    {
        using var doc = JsonDocument.Parse(viewDefinitionJson);
        return doc.RootElement.TryGetProperty("name", out var nameEl)
            ? nameEl.GetString() ?? throw new InvalidOperationException("ViewDefinition must have a 'name'.")
            : throw new InvalidOperationException("ViewDefinition must have a 'name'.");
    }
}
