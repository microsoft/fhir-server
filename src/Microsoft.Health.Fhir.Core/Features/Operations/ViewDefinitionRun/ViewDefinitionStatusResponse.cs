// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace Microsoft.Health.Fhir.Core.Features.Operations.ViewDefinitionRun;

/// <summary>
/// Response for the ViewDefinition status query, returning materialization lifecycle state.
/// </summary>
public class ViewDefinitionStatusResponse
{
    /// <summary>
    /// Gets or sets the ViewDefinition name.
    /// </summary>
    public string ViewDefinitionName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the FHIR resource type targeted by the ViewDefinition.
    /// </summary>
    public string ResourceType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the materialization status (Creating, Populating, Active, Error, Inactive).
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the error message, if status is Error.
    /// </summary>
    public string ErrorMessage { get; set; }

    /// <summary>
    /// Gets or sets the IDs of auto-created Subscription resources.
    /// </summary>
    public IReadOnlyList<string> SubscriptionIds { get; set; } = new List<string>();

    /// <summary>
    /// Gets or sets the Library resource ID that persists this ViewDefinition.
    /// </summary>
    public string LibraryResourceId { get; set; }

    /// <summary>
    /// Gets or sets when the ViewDefinition was registered.
    /// </summary>
    public DateTimeOffset RegisteredAt { get; set; }

    /// <summary>
    /// Gets or sets whether the materialized table exists.
    /// </summary>
    public bool TableExists { get; set; }

    /// <summary>
    /// Gets or sets the materialization target (e.g., SqlServer, Fabric, Parquet).
    /// </summary>
    public string Target { get; set; } = string.Empty;
}
