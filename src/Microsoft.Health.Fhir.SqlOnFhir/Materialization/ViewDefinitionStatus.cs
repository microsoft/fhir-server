// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.SqlOnFhir.Materialization;

/// <summary>
/// Represents the lifecycle state of a materialized ViewDefinition.
/// </summary>
public enum ViewDefinitionStatus
{
    /// <summary>
    /// The materialized table is being created (schema generation in progress).
    /// </summary>
    Creating,

    /// <summary>
    /// Initial population is in progress (background job scanning existing resources).
    /// </summary>
    Populating,

    /// <summary>
    /// The materialized table is fully populated and receiving incremental updates via subscriptions.
    /// </summary>
    Active,

    /// <summary>
    /// An error occurred during creation, population, or subscription setup.
    /// </summary>
    Error,

    /// <summary>
    /// The materialized table has been deactivated (subscription removed, table may still exist).
    /// </summary>
    Inactive,
}
