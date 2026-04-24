// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors;

/// <summary>
/// Describes the search behavior for a single-point search parameter.
/// </summary>
public enum SinglePointSearchBehavior
{
    /// <summary>
    /// No special behavior is defined.
    /// </summary>
    None = 0,

    /// <summary>
    /// The search parameter represents a single point in time (no end date).
    /// Requires special handling during query rewriting.
    /// </summary>
    SinglePointDateTime = 1,
}
