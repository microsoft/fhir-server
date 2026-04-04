// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.SqlOnFhir.Materialization;

/// <summary>
/// Handles incremental materialization of ViewDefinition results into SQL Server tables.
/// Supports upsert (delete-then-insert) and delete operations for individual resources.
/// </summary>
public interface IViewDefinitionMaterializer
{
    /// <summary>
    /// Updates the materialized table rows for a single resource. This performs an atomic
    /// delete-then-insert operation: all existing rows for the resource are removed, the ViewDefinition
    /// is re-evaluated against the resource, and the resulting rows are inserted.
    /// </summary>
    /// <param name="viewDefinitionJson">The ViewDefinition JSON string.</param>
    /// <param name="viewDefinitionName">The ViewDefinition name (matches the table name in the <c>sqlfhir</c> schema).</param>
    /// <param name="resource">The FHIR resource to evaluate.</param>
    /// <param name="resourceKey">The resource key used for tracking (typically <c>{ResourceType}/{ResourceId}</c>).</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The number of rows inserted into the materialized table.</returns>
    Task<int> UpsertResourceAsync(
        string viewDefinitionJson,
        string viewDefinitionName,
        ResourceElement resource,
        string resourceKey,
        CancellationToken cancellationToken);

    /// <summary>
    /// Removes all materialized table rows for a resource that has been deleted.
    /// </summary>
    /// <param name="viewDefinitionName">The ViewDefinition name (matches the table name in the <c>sqlfhir</c> schema).</param>
    /// <param name="resourceKey">The resource key used for tracking (typically <c>{ResourceType}/{ResourceId}</c>).</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The number of rows deleted from the materialized table.</returns>
    Task<int> DeleteResourceAsync(
        string viewDefinitionName,
        string resourceKey,
        CancellationToken cancellationToken);
}
