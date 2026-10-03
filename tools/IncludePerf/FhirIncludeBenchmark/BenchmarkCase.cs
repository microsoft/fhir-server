// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Internal.Fhir.IncludePerf.Benchmark
{
    /// <summary>
    /// How the benchmark authenticates a case. This determines whether the SMART compartment rewriter
    /// engages, which is the code path PR 5683 changes.
    /// </summary>
    internal enum AuthMode
    {
        /// <summary>
        /// A globalAdmin token with only the <c>fhir-api</c> audience scope. No SMART scopes, so
        /// AccessControlContext.ApplyFineGrainedAccessControl stays false and the generated SQL is the
        /// unmodified include query. Used to prove there is no regression for ordinary include traffic.
        /// </summary>
        Admin,

        /// <summary>
        /// A SMART v1 patient-scoped token (<c>patient/*.read</c>) whose fhirUser claim binds the request to a
        /// single Patient compartment. This is the primary path under test.
        /// </summary>
        SmartPatient,

        /// <summary>
        /// A SMART v2 granular-scope token (for example <c>patient/Observation.rs?category=vital-signs</c>).
        /// Granular scopes trigger the second, more expensive query shape in SqlQueryGenerator where the
        /// scope-restricted set is regenerated inside a new WITH clause for the include CTEs.
        /// </summary>
        SmartPatientV2,
    }

    /// <summary>
    /// A single benchmark case: one HTTP query shape executed under one authentication mode.
    /// </summary>
    internal sealed class BenchmarkCase
    {
        /// <summary>
        /// Gets the stable identifier used to correlate the same case across the baseline and branch runs.
        /// </summary>
        internal string Name { get; init; }

        /// <summary>
        /// Gets a short grouping label (for example "forward-include", "reverse-include", "iterate").
        /// </summary>
        internal string Group { get; init; }

        /// <summary>
        /// Gets the path and query relative to the FHIR base URL. <c>{patient}</c> is replaced with the
        /// target Patient id.
        /// </summary>
        internal string PathAndQuery { get; init; }

        internal AuthMode Auth { get; init; }

        /// <summary>
        /// Gets the SMART scope string requested for this case. Ignored for <see cref="AuthMode.Admin"/>.
        /// </summary>
        internal string Scope { get; init; }

        /// <summary>
        /// Gets a value indicating whether the case should follow the bundle's "related" link and time the
        /// resulting $includes continuation request instead of the initial search.
        /// </summary>
        internal bool FollowRelatedLink { get; init; }

        internal string Notes { get; init; }
    }
}
