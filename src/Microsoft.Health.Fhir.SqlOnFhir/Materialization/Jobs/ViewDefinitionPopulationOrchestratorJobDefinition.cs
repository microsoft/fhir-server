// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

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
}
