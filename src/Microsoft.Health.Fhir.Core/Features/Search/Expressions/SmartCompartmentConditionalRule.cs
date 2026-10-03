// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search.Expressions
{
    /// <summary>
    /// Describes how a resource type becomes visible within a SMART compartment when its visibility is
    /// conditional (that is, it is neither universally shared nor a plain formal compartment member).
    /// </summary>
    public enum SmartCompartmentConditionalVisibility
    {
        /// <summary>
        /// The resource is visible when it references the compartment root through the reference search parameter
        /// (for example, a Device whose <c>Device.patient</c> points at the compartment patient).
        /// </summary>
        ReferencesCompartmentRoot,

        /// <summary>
        /// The resource is visible when it has no value for the reference search parameter at all
        /// (for example, an unassigned Device with no <c>Device.patient</c> reference).
        /// </summary>
        HasNoReference,
    }

    /// <summary>
    /// A single declarative rule describing conditional visibility of a resource type within a SMART compartment.
    /// This is the single source of truth for both the compartment union (<see cref="SmartCompartmentSearchRewriter"/>)
    /// and the SQL include/revinclude candidate authorization predicate, so the two paths cannot drift.
    /// </summary>
    /// <param name="ResourceType">The resource type whose visibility is conditional (for example, Device).</param>
    /// <param name="ReferenceSearchParameter">The reference search parameter that governs the condition (for example, Device.patient).</param>
    /// <param name="Visibility">How the resource type becomes visible.</param>
    public sealed record SmartCompartmentConditionalRule(
        string ResourceType,
        SearchParameterInfo ReferenceSearchParameter,
        SmartCompartmentConditionalVisibility Visibility);
}
