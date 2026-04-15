// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.SqlOnFhir.Materialization;

/// <summary>
/// Handles incremental materialization of ViewDefinition results into a storage target.
/// Supports upsert and delete operations for individual resources, and storage lifecycle
/// management (provisioning and cleanup) for the materialized storage structures.
/// </summary>
public interface IViewDefinitionMaterializer
{
    /// <summary>
    /// Ensures the storage structure for the given ViewDefinition exists, creating it if necessary.
    /// For SQL Server, this creates the table in the <c>sqlfhir</c> schema.
    /// For Delta Lake, this is a no-op (tables are created on first write by <c>LoadOrCreateTableAsync</c>).
    /// </summary>
    /// <param name="viewDefinitionJson">The ViewDefinition JSON string.</param>
    /// <param name="viewDefinitionName">The ViewDefinition name (used as the storage identifier).</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns><c>true</c> if the storage was created; <c>false</c> if it already existed.</returns>
    Task<bool> EnsureStorageAsync(string viewDefinitionJson, string viewDefinitionName, CancellationToken cancellationToken);

    /// <summary>
    /// Checks whether materialized storage exists for the given ViewDefinition.
    /// For SQL Server, this checks if the table exists.
    /// For Delta Lake, this checks if the Delta table directory exists.
    /// </summary>
    /// <param name="viewDefinitionName">The ViewDefinition name (used as the storage identifier).</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns><c>true</c> if the storage exists; otherwise, <c>false</c>.</returns>
    Task<bool> StorageExistsAsync(string viewDefinitionName, CancellationToken cancellationToken);

    /// <summary>
    /// Removes the materialized storage for the given ViewDefinition.
    /// For SQL Server, this drops the table.
    /// For Delta Lake, this deletes the Delta table directory.
    /// </summary>
    /// <param name="viewDefinitionName">The ViewDefinition name (used as the storage identifier).</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task CleanupStorageAsync(string viewDefinitionName, CancellationToken cancellationToken);

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
