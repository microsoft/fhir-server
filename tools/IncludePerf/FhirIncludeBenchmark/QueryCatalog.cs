// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Internal.Fhir.IncludePerf.Benchmark
{
    /// <summary>
    /// The default catalog of query shapes exercised by the benchmark.
    /// <para>
    /// The catalog is deliberately split three ways:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// <b>admin-*</b> cases run without SMART scopes. PR 5683 leaves this SQL untouched
    /// (SqlRootExpression.SmartCompartmentMembership is null), so any movement here is environmental noise
    /// or an unintended regression in ordinary _include/_revinclude traffic.
    /// </description></item>
    /// <item><description>
    /// <b>smart-*</b> cases run under a SMART v1 patient scope. These emit the new per-candidate
    /// authorization predicate and are the primary subject of the comparison.
    /// </description></item>
    /// <item><description>
    /// <b>smartv2-*</b> cases use granular scopes with search parameters, which take the second
    /// (more expensive) code path where the scope-restricted set is regenerated in a new WITH clause.
    /// </description></item>
    /// </list>
    /// <para>
    /// Each family includes a "-noinclude" control so the include CTE cost can be isolated from the cost of
    /// the base compartment query.
    /// </para>
    /// </summary>
    internal static class QueryCatalog
    {
        internal const string AdminScope = "fhir-api";

        internal const string SmartV1Scope = "fhir-api fhirUser patient/*.read";

        internal const string SmartV2Scope =
            "fhir-api fhirUser patient/Observation.rs?category=vital-signs patient/Patient.rs patient/Encounter.rs patient/Practitioner.rs patient/Organization.rs patient/Location.rs patient/DiagnosticReport.rs patient/Condition.rs";

        internal static IReadOnlyList<BenchmarkCase> Default { get; } = new[]
        {
            // ── Controls: no includes at all ─────────────────────────────────────────────────────
            new BenchmarkCase
            {
                Name = "admin-control-noinclude",
                Group = "control",
                PathAndQuery = "Observation?subject=Patient/{patient}&_count=50",
                Auth = AuthMode.Admin,
                Notes = "Base query cost without any include CTE (non-SMART).",
            },
            new BenchmarkCase
            {
                Name = "smart-control-noinclude",
                Group = "control",
                PathAndQuery = "Observation?_count=50",
                Auth = AuthMode.SmartPatient,
                Scope = SmartV1Scope,
                Notes = "Base SMART compartment query cost without any include CTE.",
            },
            new BenchmarkCase
            {
                Name = "smartv2-control-noinclude",
                Group = "control",
                PathAndQuery = "Observation?_count=50",
                Auth = AuthMode.SmartPatientV2,
                Scope = SmartV2Scope,
                Notes = "Base SMART v2 granular-scope query cost without any include CTE.",
            },

            // ── Non-SMART include traffic (regression guard) ─────────────────────────────────────
            new BenchmarkCase
            {
                Name = "admin-fwd-observation-subject",
                Group = "forward-include",
                PathAndQuery = "Observation?subject=Patient/{patient}&_include=Observation:subject&_count=50",
                Auth = AuthMode.Admin,
            },
            new BenchmarkCase
            {
                Name = "admin-fwd-observation-multi",
                Group = "forward-include",
                PathAndQuery = "Observation?subject=Patient/{patient}&_include=Observation:encounter&_include=Observation:performer&_count=50",
                Auth = AuthMode.Admin,
            },
            new BenchmarkCase
            {
                Name = "admin-fwd-wildcard",
                Group = "forward-include",
                PathAndQuery = "Observation?subject=Patient/{patient}&_include=*&_count=50",
                Auth = AuthMode.Admin,
            },
            new BenchmarkCase
            {
                Name = "admin-rev-observation",
                Group = "reverse-include",
                PathAndQuery = "Patient?_id={patient}&_revinclude=Observation:subject&_count=10",
                Auth = AuthMode.Admin,
            },
            new BenchmarkCase
            {
                Name = "admin-rev-wildcard",
                Group = "reverse-include",
                PathAndQuery = "Patient?_id={patient}&_revinclude=*&_count=10",
                Auth = AuthMode.Admin,
            },
            new BenchmarkCase
            {
                Name = "admin-rev-encounter-observation",
                Group = "reverse-include",
                PathAndQuery = "Encounter?subject=Patient/{patient}&_revinclude=Observation:encounter&_count=20",
                Auth = AuthMode.Admin,
            },
            new BenchmarkCase
            {
                Name = "admin-iterate-diagnosticreport",
                Group = "iterate",
                PathAndQuery = "DiagnosticReport?subject=Patient/{patient}&_include=DiagnosticReport:result&_include:iterate=Observation:subject&_count=20",
                Auth = AuthMode.Admin,
            },
            new BenchmarkCase
            {
                Name = "admin-reviterate-diagnosticreport",
                Group = "iterate",
                PathAndQuery = "Patient?_id={patient}&_revinclude=Observation:subject&_revinclude:iterate=DiagnosticReport:result&_count=10",
                Auth = AuthMode.Admin,
                Notes = "Reverse iterate: Patient <- Observation <- DiagnosticReport.",
            },
            new BenchmarkCase
            {
                Name = "admin-iterate-3hop",
                Group = "iterate",
                PathAndQuery = "Observation?subject=Patient/{patient}&_include=Observation:encounter&_include:iterate=Encounter:practitioner&_include:iterate=Encounter:service-provider&_count=50",
                Auth = AuthMode.Admin,
                Notes = "Three include CTEs; each iterate hop emits its own CTE and its own authorization predicate.",
            },
            new BenchmarkCase
            {
                Name = "admin-includes-paging",
                Group = "includes-operation",
                PathAndQuery = "Patient?_id={patient}&_revinclude=*&_includesCount=20&_count=1",
                Auth = AuthMode.Admin,
                FollowRelatedLink = true,
                Notes = "Times the $includes continuation request reached through the bundle's 'related' link.",
            },

            // ── Fully-qualified wildcards (*:*) ──────────────────────────────────────────────────
            // ExpressionParser.ParseInclude treats "*" and "*:*" differently: on a forward include "*"
            // leaves SourceResourceType null whereas "*:*" sets it to "*". That value feeds
            // TryGetIncludeCtes on the iterate path, so these are genuinely distinct query shapes rather
            // than duplicates of the "*" cases above. "*:*" is also the form IncludesOperationTests uses.
            new BenchmarkCase
            {
                Name = "admin-fwd-wildcard-qualified",
                Group = "wildcard-qualified",
                PathAndQuery = "Observation?subject=Patient/{patient}&_include=*:*&_count=50",
                Auth = AuthMode.Admin,
            },
            new BenchmarkCase
            {
                Name = "admin-rev-wildcard-qualified",
                Group = "wildcard-qualified",
                PathAndQuery = "Patient?_id={patient}&_revinclude=*:*&_count=10",
                Auth = AuthMode.Admin,
            },
            new BenchmarkCase
            {
                Name = "admin-bidir-wildcard-qualified",
                Group = "wildcard-qualified",
                PathAndQuery = "Patient?_id={patient}&_include=*:*&_revinclude=*:*&_count=10",
                Auth = AuthMode.Admin,
                Notes = "Both directions at once - the IncludesOperationTests shape and the widest candidate set.",
            },
            new BenchmarkCase
            {
                Name = "admin-iterate-wildcard-qualified",
                Group = "wildcard-qualified",
                PathAndQuery = "DiagnosticReport?subject=Patient/{patient}&_include=*:*&_include:iterate=Observation:subject&_count=20",
                Auth = AuthMode.Admin,
                Notes = "Wildcard include feeding a typed iterate hop. A wildcard ON the iterate itself (_include:iterate=* or *:*) is rejected by the server with 'Resource type * is not supported', on both baseline and branch, so it is a pre-existing limitation rather than something this change can regress.",
            },

            // ── SMART v1 patient scope (primary subject of the comparison) ───────────────────────
            new BenchmarkCase
            {
                Name = "smart-fwd-observation-subject",
                Group = "forward-include",
                PathAndQuery = "Observation?_include=Observation:subject&_count=50",
                Auth = AuthMode.SmartPatient,
                Scope = SmartV1Scope,
            },
            new BenchmarkCase
            {
                Name = "smart-fwd-observation-multi",
                Group = "forward-include",
                PathAndQuery = "Observation?_include=Observation:encounter&_include=Observation:performer&_count=50",
                Auth = AuthMode.SmartPatient,
                Scope = SmartV1Scope,
            },
            new BenchmarkCase
            {
                Name = "smart-fwd-wildcard",
                Group = "forward-include",
                PathAndQuery = "Observation?_include=*&_count=50",
                Auth = AuthMode.SmartPatient,
                Scope = SmartV1Scope,
            },
            new BenchmarkCase
            {
                Name = "smart-fwd-medicationrequest-universal",
                Group = "forward-include",
                PathAndQuery = "MedicationRequest?_include=MedicationRequest:medication&_include=MedicationRequest:requester&_count=50",
                Auth = AuthMode.SmartPatient,
                Scope = SmartV1Scope,
                Notes = "Include targets are universal types (Medication, Practitioner) - the cheap IN (...) leg of the predicate.",
            },
            new BenchmarkCase
            {
                Name = "smart-rev-observation",
                Group = "reverse-include",
                PathAndQuery = "Patient?_revinclude=Observation:subject&_count=10",
                Auth = AuthMode.SmartPatient,
                Scope = SmartV1Scope,
            },
            new BenchmarkCase
            {
                Name = "smart-rev-wildcard",
                Group = "reverse-include",
                PathAndQuery = "Patient?_revinclude=*&_count=10",
                Auth = AuthMode.SmartPatient,
                Scope = SmartV1Scope,
                Notes = "Worst case: every referencing type must be authorized per candidate row.",
            },
            new BenchmarkCase
            {
                Name = "smart-rev-encounter-observation",
                Group = "reverse-include",
                PathAndQuery = "Encounter?_revinclude=Observation:encounter&_count=20",
                Auth = AuthMode.SmartPatient,
                Scope = SmartV1Scope,
            },
            new BenchmarkCase
            {
                Name = "smart-rev-device",
                Group = "reverse-include",
                PathAndQuery = "Patient?_revinclude=Device:patient&_count=10",
                Auth = AuthMode.SmartPatient,
                Scope = SmartV1Scope,
                Notes = "Exercises the conditional-visibility rules (EXISTS own-device / NOT EXISTS unassigned).",
            },
            new BenchmarkCase
            {
                Name = "smart-iterate-diagnosticreport",
                Group = "iterate",
                PathAndQuery = "DiagnosticReport?_include=DiagnosticReport:result&_include:iterate=Observation:subject&_count=20",
                Auth = AuthMode.SmartPatient,
                Scope = SmartV1Scope,
            },
            new BenchmarkCase
            {
                Name = "smart-iterate-condition-encounter",
                Group = "iterate",
                PathAndQuery = "Condition?_include=Condition:encounter&_include:iterate=Encounter:practitioner&_count=50",
                Auth = AuthMode.SmartPatient,
                Scope = SmartV1Scope,
            },
            new BenchmarkCase
            {
                Name = "smart-reviterate-diagnosticreport",
                Group = "iterate",
                PathAndQuery = "Patient?_revinclude=Observation:subject&_revinclude:iterate=DiagnosticReport:result&_count=10",
                Auth = AuthMode.SmartPatient,
                Scope = SmartV1Scope,
                Notes = "Reverse iterate under SMART. The second hop must be compartment-checked independently: a DiagnosticReport belonging to another patient can reference this patient's Observation.",
            },
            new BenchmarkCase
            {
                Name = "smart-iterate-3hop",
                Group = "iterate",
                PathAndQuery = "Observation?_include=Observation:encounter&_include:iterate=Encounter:practitioner&_include:iterate=Encounter:service-provider&_count=50",
                Auth = AuthMode.SmartPatient,
                Scope = SmartV1Scope,
                Notes = "Three include CTEs, each carrying the per-candidate authorization predicate - the deepest fan-out in the catalog.",
            },
            new BenchmarkCase
            {
                Name = "smartv2-reviterate-diagnosticreport",
                Group = "iterate",
                PathAndQuery = "Patient?_revinclude=Observation:subject&_revinclude:iterate=DiagnosticReport:result&_count=10",
                Auth = AuthMode.SmartPatientV2,
                Scope = SmartV2Scope,
                Notes = "Reverse iterate on the SMART v2 granular-scope path, where include CTEs are regenerated in a second WITH clause.",
            },
            new BenchmarkCase
            {
                Name = "smart-includes-paging",
                Group = "includes-operation",
                PathAndQuery = "Patient?_revinclude=*&_includesCount=20&_count=1",
                Auth = AuthMode.SmartPatient,
                Scope = SmartV1Scope,
                FollowRelatedLink = true,
                Notes = "Times the $includes continuation request under a SMART patient scope.",
            },

            // ── SMART fully-qualified wildcards (*:*) ────────────────────────────────────────────
            new BenchmarkCase
            {
                Name = "smart-fwd-wildcard-qualified",
                Group = "wildcard-qualified",
                PathAndQuery = "Observation?_include=*:*&_count=50",
                Auth = AuthMode.SmartPatient,
                Scope = SmartV1Scope,
            },
            new BenchmarkCase
            {
                Name = "smart-rev-wildcard-qualified",
                Group = "wildcard-qualified",
                PathAndQuery = "Patient?_revinclude=*:*&_count=10",
                Auth = AuthMode.SmartPatient,
                Scope = SmartV1Scope,
            },
            new BenchmarkCase
            {
                Name = "smart-bidir-wildcard-qualified",
                Group = "wildcard-qualified",
                PathAndQuery = "Patient?_include=*:*&_revinclude=*:*&_count=10",
                Auth = AuthMode.SmartPatient,
                Scope = SmartV1Scope,
                Notes = "Widest possible candidate set under SMART - every referencing AND referenced type must be authorized per row, including Device.",
            },
            new BenchmarkCase
            {
                Name = "smart-iterate-wildcard-qualified",
                Group = "wildcard-qualified",
                PathAndQuery = "DiagnosticReport?_include=*:*&_include:iterate=Observation:subject&_count=20",
                Auth = AuthMode.SmartPatient,
                Scope = SmartV1Scope,
                Notes = "Wildcard include feeding a typed iterate hop under SMART; every wildcard candidate and every iterate candidate is authorized per row.",
            },
            new BenchmarkCase
            {
                Name = "smartv2-bidir-wildcard-qualified",
                Group = "wildcard-qualified",
                PathAndQuery = "Patient?_include=*:*&_revinclude=*:*&_count=10",
                Auth = AuthMode.SmartPatientV2,
                Scope = SmartV2Scope,
                Notes = "Widest candidate set on the SMART v2 granular-scope path.",
            },

            // ── SMART v2 granular scopes ─────────────────────────────────────────────────────────
            new BenchmarkCase
            {
                Name = "smartv2-fwd-observation-subject",
                Group = "forward-include",
                PathAndQuery = "Observation?_include=Observation:subject&_count=50",
                Auth = AuthMode.SmartPatientV2,
                Scope = SmartV2Scope,
            },
            new BenchmarkCase
            {
                Name = "smartv2-rev-observation",
                Group = "reverse-include",
                PathAndQuery = "Patient?_revinclude=Observation:subject&_count=10",
                Auth = AuthMode.SmartPatientV2,
                Scope = SmartV2Scope,
            },
        };
    }
}
