// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search.Expressions;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions
{
    /// <summary>
    /// SQL-layer projection of a <see cref="SmartCompartmentConditionalRule"/>: a resource type whose SMART
    /// compartment visibility is conditional, lowered to values the SQL query generator can consume directly.
    /// </summary>
    /// <param name="ResourceType">The resource type whose visibility is conditional (for example, Device).</param>
    /// <param name="ReferenceSearchParameterUrl">The reference search parameter URL that governs the condition (for example, Device.patient).</param>
    /// <param name="Visibility">How the resource type becomes visible.</param>
    internal sealed record SmartCompartmentConditionalMembershipRule(
        string ResourceType,
        string ReferenceSearchParameterUrl,
        SmartCompartmentConditionalVisibility Visibility);
}
