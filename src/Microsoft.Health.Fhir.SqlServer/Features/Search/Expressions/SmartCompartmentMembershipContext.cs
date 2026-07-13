// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Immutable;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions
{
    /// <summary>
    /// Immutable SQL-layer description of SMART compartment membership rules.
    /// </summary>
    internal sealed record SmartCompartmentMembershipContext(
        string CompartmentResourceType,
        string CompartmentResourceId,
        ImmutableArray<string> SharedResourceTypes,
        ImmutableArray<SmartCompartmentMembershipRule> MembershipRules);
}
