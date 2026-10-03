// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Internal.Fhir.IncludePerf.DataGenerator
{
    /// <summary>
    /// Sizing knobs for a generated dataset. The defaults describe the per-patient compartment shape;
    /// "heavy" patients multiply those counts so the benchmark can measure both a typical and a
    /// worst-case include fan-out.
    /// </summary>
    internal sealed class DatasetProfile
    {
        internal string Name { get; init; } = "large";

        internal int PatientCount { get; init; } = 25_000;

        /// <summary>
        /// Gets the number of leading patients that receive <see cref="HeavyPatientMultiplier"/> times the
        /// normal resource counts. These are the worst-case compartments the benchmark targets.
        /// </summary>
        internal int HeavyPatientCount { get; init; } = 25;

        internal int HeavyPatientMultiplier { get; init; } = 10;

        internal int EncountersPerPatient { get; init; } = 6;

        internal int ObservationsPerPatient { get; init; } = 90;

        internal int ConditionsPerPatient { get; init; } = 8;

        internal int MedicationRequestsPerPatient { get; init; } = 12;

        internal int DiagnosticReportsPerPatient { get; init; } = 8;

        internal int DocumentReferencesPerPatient { get; init; } = 6;

        internal int ProceduresPerPatient { get; init; } = 10;

        internal int AllergiesPerPatient { get; init; } = 4;

        internal int ImmunizationsPerPatient { get; init; } = 6;

        internal int CarePlansPerPatient { get; init; } = 2;

        internal int DevicesPerPatient { get; init; } = 1;

        /// <summary>
        /// Gets the percentage of resources that deliberately reference a NEIGHBOURING patient's data,
        /// creating genuine cross-compartment links.
        /// <para>
        /// Without these, every reference stays inside a single patient compartment, the SMART
        /// authorization predicate never excludes anything, and the baseline and branch return identical
        /// results - which would make it impossible to prove the compartment fix works at scale.
        /// </para>
        /// <para>Three leak shapes are produced, mirroring the scenarios in the PR's integration tests:</para>
        /// <list type="number">
        /// <item><description>
        /// Patient P's DiagnosticReport references patient P-1's Observation. Reachable from P-1's
        /// compartment via <c>_revinclude=Observation:subject&amp;_revinclude:iterate=DiagnosticReport:result</c>.
        /// </description></item>
        /// <item><description>
        /// Patient P's Observation carries <c>focus</c> -&gt; Patient P+1. Reachable from P's compartment via
        /// <c>_include=DiagnosticReport:result&amp;_include:iterate=Observation:focus</c>.
        /// </description></item>
        /// <item><description>
        /// Patient P's Observation carries <c>device</c> -&gt; a Device owned by Patient P+1, which exercises
        /// the conditional-visibility (Device) rules via <c>_include=Observation:device</c>.
        /// </description></item>
        /// </list>
        /// </summary>
        internal int CrossCompartmentPercent { get; init; } = 20;

        /// <summary>
        /// Gets the number of Observations each DiagnosticReport links through <c>DiagnosticReport.result</c>.
        /// This drives the <c>_include:iterate</c> depth used by the benchmark.
        /// </summary>
        internal int ResultsPerDiagnosticReport { get; init; } = 3;

        /// <summary>
        /// Gets the percentage of Devices that carry a <c>Device.patient</c> reference. The remainder have no
        /// patient reference at all, which exercises both legs of the SMART conditional-visibility rules
        /// (the EXISTS "own device" leg and the NOT EXISTS "unassigned device" leg).
        /// </summary>
        internal int DeviceWithPatientPercent { get; init; } = 60;

        internal int PractitionerCount { get; init; } = 2_000;

        internal int OrganizationCount { get; init; } = 200;

        internal int LocationCount { get; init; } = 500;

        internal int MedicationCount { get; init; } = 300;

        /// <summary>
        /// Gets the maximum number of NDJSON lines written to a single shard file. Smaller files let
        /// $import parallelize more aggressively.
        /// </summary>
        internal int MaxLinesPerFile { get; init; } = 250_000;

        internal static DatasetProfile ForName(string name)
        {
            switch (name.ToLowerInvariant())
            {
                case "small":
                    return new DatasetProfile
                    {
                        Name = "small",
                        PatientCount = 1_000,
                        HeavyPatientCount = 10,
                        PractitionerCount = 200,
                        OrganizationCount = 40,
                        LocationCount = 60,
                        MedicationCount = 60,
                    };

                case "medium":
                    return new DatasetProfile
                    {
                        Name = "medium",
                        PatientCount = 5_000,
                        HeavyPatientCount = 15,
                        PractitionerCount = 600,
                        OrganizationCount = 80,
                        LocationCount = 150,
                        MedicationCount = 120,
                    };

                case "large":
                    return new DatasetProfile { Name = "large" };

                default:
                    throw new ArgumentException($"Unknown profile '{name}'. Expected small, medium or large.", nameof(name));
            }
        }

        /// <summary>
        /// Returns the multiplier applied to per-patient resource counts for the given patient index.
        /// </summary>
        internal int MultiplierFor(int patientIndex) => patientIndex < HeavyPatientCount ? HeavyPatientMultiplier : 1;

        /// <summary>
        /// Estimates the total resource count so the caller can report progress and size storage.
        /// </summary>
        internal long EstimateResourceCount()
        {
            long perPatient = 1 + EncountersPerPatient + ObservationsPerPatient + ConditionsPerPatient +
                MedicationRequestsPerPatient + DiagnosticReportsPerPatient + DocumentReferencesPerPatient +
                ProceduresPerPatient + AllergiesPerPatient + ImmunizationsPerPatient + CarePlansPerPatient +
                DevicesPerPatient;

            // The Patient resource itself is not multiplied for heavy patients.
            long heavyExtra = (long)HeavyPatientCount * (perPatient - 1) * (HeavyPatientMultiplier - 1);

            return (perPatient * PatientCount) + heavyExtra +
                PractitionerCount + OrganizationCount + LocationCount + MedicationCount;
        }
    }
}
