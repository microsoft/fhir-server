// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.ObjectModel;
using Microsoft.Health.Fhir.SqlOnFhir.Materialization;

namespace Microsoft.Health.Fhir.SqlOnFhir.Channels;

/// <summary>
/// Tracks the registration of a ViewDefinition for materialization,
/// including its associated auto-created Subscription resource(s).
/// </summary>
public sealed class ViewDefinitionRegistration
{
    /// <summary>
    /// Gets or sets the ViewDefinition JSON string.
    /// </summary>
    public required string ViewDefinitionJson { get; set; }

    /// <summary>
    /// Gets or sets the ViewDefinition name (used as the SQL table name).
    /// </summary>
    public required string ViewDefinitionName { get; set; }

    /// <summary>
    /// Gets or sets the FHIR resource type targeted by the ViewDefinition.
    /// </summary>
    public required string ResourceType { get; set; }

    /// <summary>
    /// Gets or sets the materialization target for this ViewDefinition.
    /// </summary>
    public MaterializationTarget Target { get; set; } = MaterializationTarget.SqlServer;

    /// <summary>
    /// Gets or sets the current lifecycle status of this materialized ViewDefinition.
    /// </summary>
    public ViewDefinitionStatus Status { get; set; } = ViewDefinitionStatus.Creating;

    /// <summary>
    /// Gets or sets the error message if <see cref="Status"/> is <see cref="ViewDefinitionStatus.Error"/>.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Gets or sets the timestamp when this ViewDefinition was registered.
    /// </summary>
    public DateTimeOffset RegisteredAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets the list of Subscription resource IDs auto-created for this ViewDefinition.
    /// </summary>
    public Collection<string> SubscriptionIds { get; } = new();
}
