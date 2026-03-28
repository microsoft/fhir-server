// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.SqlOnFhir.Materialization.Jobs;

/// <summary>
/// Result of a ViewDefinition population processing job.
/// </summary>
public class ViewDefinitionPopulationProcessingJobResult
{
    /// <summary>
    /// Gets or sets the total number of resources evaluated.
    /// </summary>
    public long ResourcesProcessed { get; set; }

    /// <summary>
    /// Gets or sets the total number of rows inserted into the materialized table.
    /// </summary>
    public long RowsInserted { get; set; }

    /// <summary>
    /// Gets or sets the number of resources that failed evaluation.
    /// </summary>
    public long FailedResources { get; set; }

    /// <summary>
    /// Gets or sets the continuation token for the next batch, if more resources remain.
    /// Null when the processing job has completed all resources in its assigned scope.
    /// </summary>
    public string? NextContinuationToken { get; set; }
}
