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
    /// <param name="CompartmentResourceType">The compartment root resource type (for example, Patient or Practitioner).</param>
    /// <param name="CompartmentResourceId">The authorized compartment root resource id.</param>
    /// <param name="SharedResourceTypes">Resource types that are always authorized as include candidates (universal/shared).</param>
    /// <param name="MembershipRules">Formal compartment membership rules grouped by candidate resource type.</param>
    /// <param name="ConditionalRules">Rules for resource types whose visibility is conditional (neither universally shared nor plain members), for example the SMART Device limits. Each rule authorizes a candidate that either references the compartment root or has no reference at all, depending on its visibility.</param>
    internal sealed record SmartCompartmentMembershipContext(
        string CompartmentResourceType,
        string CompartmentResourceId,
        ImmutableArray<string> SharedResourceTypes,
        ImmutableArray<SmartCompartmentMembershipRule> MembershipRules,
        ImmutableArray<SmartCompartmentConditionalMembershipRule> ConditionalRules = default);
}
