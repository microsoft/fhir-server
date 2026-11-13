// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Internal.Fhir.IncludePerf.DataGenerator
{
    /// <summary>
    /// Owns one <see cref="ShardedNdjsonWriter"/> per resource type for a single generator worker.
    /// </summary>
    internal sealed class WriterSet : IDisposable
    {
        internal WriterSet(string outputDirectory, int workerId, int maxLinesPerFile)
        {
            ShardedNdjsonWriter Create(string resourceType) =>
                new(outputDirectory, resourceType, workerId, maxLinesPerFile);

            Patient = Create("Patient");
            Encounter = Create("Encounter");
            Observation = Create("Observation");
            Condition = Create("Condition");
            MedicationRequest = Create("MedicationRequest");
            DiagnosticReport = Create("DiagnosticReport");
            DocumentReference = Create("DocumentReference");
            Procedure = Create("Procedure");
            AllergyIntolerance = Create("AllergyIntolerance");
            Immunization = Create("Immunization");
            CarePlan = Create("CarePlan");
            Device = Create("Device");
            Practitioner = Create("Practitioner");
            Organization = Create("Organization");
            Location = Create("Location");
            Medication = Create("Medication");

            All = new[]
            {
                Patient, Encounter, Observation, Condition, MedicationRequest, DiagnosticReport,
                DocumentReference, Procedure, AllergyIntolerance, Immunization, CarePlan, Device,
                Practitioner, Organization, Location, Medication,
            };
        }

        internal ShardedNdjsonWriter Patient { get; }

        internal ShardedNdjsonWriter Encounter { get; }

        internal ShardedNdjsonWriter Observation { get; }

        internal ShardedNdjsonWriter Condition { get; }

        internal ShardedNdjsonWriter MedicationRequest { get; }

        internal ShardedNdjsonWriter DiagnosticReport { get; }

        internal ShardedNdjsonWriter DocumentReference { get; }

        internal ShardedNdjsonWriter Procedure { get; }

        internal ShardedNdjsonWriter AllergyIntolerance { get; }

        internal ShardedNdjsonWriter Immunization { get; }

        internal ShardedNdjsonWriter CarePlan { get; }

        internal ShardedNdjsonWriter Device { get; }

        internal ShardedNdjsonWriter Practitioner { get; }

        internal ShardedNdjsonWriter Organization { get; }

        internal ShardedNdjsonWriter Location { get; }

        internal ShardedNdjsonWriter Medication { get; }

        internal IReadOnlyList<ShardedNdjsonWriter> All { get; }

        public void Dispose()
        {
            foreach (ShardedNdjsonWriter writer in All)
            {
                writer.Dispose();
            }
        }
    }
}
