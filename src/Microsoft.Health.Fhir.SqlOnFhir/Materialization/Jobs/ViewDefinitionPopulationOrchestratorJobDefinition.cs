// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.JobManagement;

namespace Microsoft.Health.Fhir.SqlOnFhir.Materialization.Jobs;

/// <summary>
/// Definition for the ViewDefinition population orchestrator job.
/// Submitted when a new ViewDefinition is registered for materialization.
/// </summary>
public class ViewDefinitionPopulationOrchestratorJobDefinition : IJobData
{
    /// <inheritdoc />
    public int TypeId { get; set; } = (int)JobType.ViewDefinitionPopulationOrchestrator;

    /// <summary>
    /// Gets or sets a unique identifier for this registration attempt.
    /// Ensures each registration produces a unique definition hash so the job queue
    /// does not deduplicate against completed jobs from previous runs.
    /// </summary>
    public string RegistrationId { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Gets or sets the ViewDefinition JSON string.
    /// </summary>
    public string ViewDefinitionJson { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the ViewDefinition name (used as the SQL table name).
    /// </summary>
    public string ViewDefinitionName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the FHIR resource type targeted by the ViewDefinition (e.g., "Patient", "Observation").
    /// </summary>
    public string ResourceType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the maximum number of resources to process per batch.
    /// </summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>
    /// Gets or sets the Library resource ID that contains this ViewDefinition.
    /// Propagated through the job chain so the processing job can persist status
    /// back to the Library resource, enabling cross-node status updates.
    /// </summary>
    public string? LibraryResourceId { get; set; }
}
