// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.JobManagement;

namespace Microsoft.Health.Fhir.SqlOnFhir.Materialization.Jobs;

/// <summary>
/// Definition for a ViewDefinition population processing job.
/// Each processing job handles a batch of resources identified by a continuation token or ID range.
/// </summary>
public class ViewDefinitionPopulationProcessingJobDefinition : IJobData
{
    /// <inheritdoc />
    public int TypeId { get; set; } = (int)JobType.ViewDefinitionPopulationProcessing;

    /// <summary>
    /// Gets or sets a unique identifier for this registration attempt.
    /// Propagated from the orchestrator to ensure each processing job also gets
    /// a unique definition hash, preventing deduplication against previous runs.
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
    /// Gets or sets the FHIR resource type targeted by the ViewDefinition.
    /// </summary>
    public string ResourceType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the maximum number of resources to process per search query.
    /// </summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>
    /// Gets or sets the continuation token for resuming search from a previous batch.
    /// Null for the first batch.
    /// </summary>
    public string? ContinuationToken { get; set; }

    /// <summary>
    /// Gets or sets the Library resource ID that contains this ViewDefinition.
    /// Propagated from the orchestrator so the processing job can persist status
    /// back to the Library resource, enabling cross-node status updates.
    /// </summary>
    public string? LibraryResourceId { get; set; }
}
