// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.SqlOnFhir.Materialization;

namespace Microsoft.Health.Fhir.SqlOnFhir.Channels;

/// <summary>
/// Manages the lifecycle of auto-created Subscription resources for materialized ViewDefinitions.
/// </summary>
public interface IViewDefinitionSubscriptionManager
{
    /// <summary>
    /// Registers a ViewDefinition for materialization: creates the SQL table, enqueues the
    /// full population job, and creates Subscription resource(s) via the MediatR pipeline so
    /// the subscription engine starts sending change events to the ViewDefinitionRefreshChannel.
    /// The caller must provide the Library resource ID that persists this ViewDefinition.
    /// </summary>
    /// <param name="viewDefinitionJson">The ViewDefinition JSON string.</param>
    /// <param name="libraryResourceId">The ID of the Library resource that persists this ViewDefinition.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <param name="target">
    /// The materialization target for this ViewDefinition. If <c>null</c>, uses the server-wide
    /// <see cref="SqlOnFhirMaterializationConfiguration.DefaultTarget"/> from configuration.
    /// </param>
    /// <returns>The registration details including auto-created Subscription IDs.</returns>
    Task<ViewDefinitionRegistration> RegisterAsync(string viewDefinitionJson, string libraryResourceId, CancellationToken cancellationToken, MaterializationTarget? target = null);

    /// <summary>
    /// Unregisters a ViewDefinition: deletes the auto-created Subscription resource(s) and
    /// optionally drops the materialized SQL table.
    /// </summary>
    /// <param name="viewDefinitionName">The ViewDefinition name.</param>
    /// <param name="dropTable">Whether to drop the materialized SQL table.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task UnregisterAsync(string viewDefinitionName, bool dropTable, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the registration for a ViewDefinition, if it exists.
    /// </summary>
    /// <param name="viewDefinitionName">The ViewDefinition name.</param>
    /// <returns>The registration, or null if not registered.</returns>
    ViewDefinitionRegistration? GetRegistration(string viewDefinitionName);

    /// <summary>
    /// Gets all active ViewDefinition registrations.
    /// </summary>
    /// <returns>All active registrations.</returns>
    IReadOnlyList<ViewDefinitionRegistration> GetAllRegistrations();

    /// <summary>
    /// Adopts a ViewDefinition registration into the in-memory cache without creating SQL tables,
    /// subscriptions, or Library resources. Used by the sync service when picking up changes
    /// made by another node. Optionally verifies the materialized table exists as a sanity check.
    /// </summary>
    /// <param name="viewDefinitionJson">The ViewDefinition JSON string.</param>
    /// <param name="libraryResourceId">The Library resource ID that persists this ViewDefinition.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <param name="initialStatus">
    /// The initial status to assign. Defaults to <see cref="ViewDefinitionStatus.Active"/>.
    /// Pass the status read from the Library resource's materialization-status extension so that
    /// ViewDefinitions that were still populating before a restart remain in <c>Populating</c> state.
    /// </param>
    /// <param name="subscriptionIds">
    /// Subscription resource IDs read from the Library's <c>relatedArtifact</c> entries.
    /// These are needed for cleanup when the ViewDefinition is later deleted.
    /// </param>
    /// <param name="target">
    /// The materialization target read from the Library resource's materialization-target extension.
    /// If <c>null</c>, defaults to <see cref="MaterializationTarget.SqlServer"/>.
    /// </param>
    /// <returns>The adopted registration.</returns>
    Task<ViewDefinitionRegistration> AdoptAsync(
        string viewDefinitionJson,
        string? libraryResourceId,
        CancellationToken cancellationToken,
        ViewDefinitionStatus initialStatus = ViewDefinitionStatus.Active,
        IReadOnlyList<string>? subscriptionIds = null,
        MaterializationTarget? target = null);

    /// <summary>
    /// Removes a ViewDefinition from the in-memory cache without deleting SQL tables, subscriptions,
    /// or Library resources. Used by the sync service when another node has already handled cleanup.
    /// </summary>
    /// <param name="viewDefinitionName">The ViewDefinition name.</param>
    void Evict(string viewDefinitionName);
}
