// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using System.Text;

namespace Microsoft.Health.Internal.Fhir.IncludePerf.DataGenerator
{
    /// <summary>
    /// Emits the resources belonging to a single patient compartment, plus the shared/universal resources.
    /// Reference topology is chosen so that forward includes, reverse includes and <c>:iterate</c> chains all
    /// have meaningful fan-out:
    /// <code>
    /// Patient           -> Practitioner (general-practitioner), Organization (organization)
    /// Encounter         -> Patient, Practitioner, Organization, Location
    /// Observation       -> Patient, Encounter, Practitioner
    /// Condition         -> Patient, Encounter, Practitioner
    /// MedicationRequest -> Patient, Encounter, Practitioner, Medication
    /// DiagnosticReport  -> Patient, Encounter, Organization, Observation[] (drives _include:iterate)
    /// DocumentReference -> Patient, Encounter, Practitioner, Organization
    /// Procedure         -> Patient, Encounter, Practitioner
    /// AllergyIntolerance-> Patient, Practitioner
    /// Immunization      -> Patient, Encounter, Practitioner, Location
    /// CarePlan          -> Patient, Encounter, Practitioner
    /// Device            -> Patient (only for a configurable percentage; the rest are unassigned)
    /// </code>
    /// </summary>
    internal sealed class CompartmentGenerator
    {
        private readonly DatasetProfile _profile;
        private readonly WriterSet _writers;

        internal CompartmentGenerator(DatasetProfile profile, WriterSet writers)
        {
            _profile = profile;
            _writers = writers;
        }

        /// <summary>
        /// Builds a Patient id. The id deliberately contains the literal "patient" because the development
        /// identity provider derives the <c>fhirUser</c> claim from the client id
        /// (see OpenIddictAuthorizationController.CreateFhirUserClaim): a client id containing "patient"
        /// yields <c>https://{host}/Patient/{clientId}</c>. Registering a client application whose id equals
        /// this Patient id is what lets the benchmark obtain a real SMART patient-scoped token.
        /// </summary>
        internal static string PatientId(int i) => $"perf-patient-{i:D6}";

        internal static string PractitionerId(int i) => $"perf-prac-{i:D5}";

        internal static string OrganizationId(int i) => $"perf-org-{i:D4}";

        internal static string LocationId(int i) => $"perf-loc-{i:D4}";

        internal static string MedicationId(int i) => $"perf-med-{i:D4}";

        /// <summary>
        /// Writes every resource in one patient's compartment.
        /// </summary>
        internal void WritePatientCompartment(int patientIndex)
        {
            int multiplier = _profile.MultiplierFor(patientIndex);
            string patientId = PatientId(patientIndex);
            string patientRef = $"Patient/{patientId}";

            // Neighbouring compartments used to create genuine cross-compartment references.
            int nextPatient = (patientIndex + 1) % _profile.PatientCount;
            int previousPatient = (patientIndex - 1 + _profile.PatientCount) % _profile.PatientCount;

            int practitionerSeed = patientIndex % _profile.PractitionerCount;
            int organizationSeed = patientIndex % _profile.OrganizationCount;

            var sb = new StringBuilder(2048);

            // ── Patient ──────────────────────────────────────────────────────────────────────────
            sb.Clear();
            sb.Append("{\"resourceType\":\"Patient\",\"id\":\"").Append(patientId).Append("\",\"active\":true")
              .Append(",\"name\":[{\"family\":\"Perf").Append(patientIndex).Append("\",\"given\":[\"Test\"]}]")
              .Append(",\"gender\":\"").Append((patientIndex % 2) == 0 ? "male" : "female").Append('"')
              .Append(",\"birthDate\":\"").Append(BirthDate(patientIndex)).Append('"')
              .Append(",\"generalPractitioner\":[{\"reference\":\"Practitioner/").Append(PractitionerId(practitionerSeed)).Append("\"}]")
              .Append(",\"managingOrganization\":{\"reference\":\"Organization/").Append(OrganizationId(organizationSeed)).Append("\"}}");
            _writers.Patient.WriteLine(sb.ToString());

            // ── Encounters ───────────────────────────────────────────────────────────────────────
            int encounterCount = _profile.EncountersPerPatient * multiplier;
            for (int n = 0; n < encounterCount; n++)
            {
                string encounterId = $"perf-enc-{patientIndex:D6}-{n:D4}";
                string prac = PractitionerId((practitionerSeed + n) % _profile.PractitionerCount);
                string org = OrganizationId((organizationSeed + n) % _profile.OrganizationCount);
                string loc = LocationId((patientIndex + n) % _profile.LocationCount);

                sb.Clear();
                sb.Append("{\"resourceType\":\"Encounter\",\"id\":\"").Append(encounterId).Append('"')
                  .Append(",\"status\":\"finished\",\"class\":{\"system\":\"http://terminology.hl7.org/CodeSystem/v3-ActCode\",\"code\":\"AMB\",\"display\":\"ambulatory\"}")
                  .Append(",\"subject\":{\"reference\":\"").Append(patientRef).Append("\"}")
                  .Append(",\"participant\":[{\"individual\":{\"reference\":\"Practitioner/").Append(prac).Append("\"}}]")
                  .Append(",\"period\":{\"start\":\"").Append(Timestamp(patientIndex, n)).Append("\"}")
                  .Append(",\"serviceProvider\":{\"reference\":\"Organization/").Append(org).Append("\"}")
                  .Append(",\"location\":[{\"location\":{\"reference\":\"Location/").Append(loc).Append("\"}}]}");
                _writers.Encounter.WriteLine(sb.ToString());
            }

            // ── Observations ─────────────────────────────────────────────────────────────────────
            int observationCount = _profile.ObservationsPerPatient * multiplier;
            for (int n = 0; n < observationCount; n++)
            {
                string observationId = ObservationId(patientIndex, n);
                string enc = $"perf-enc-{patientIndex:D6}-{n % Math.Max(encounterCount, 1):D4}";
                string prac = PractitionerId((practitionerSeed + n) % _profile.PractitionerCount);

                sb.Clear();
                sb.Append("{\"resourceType\":\"Observation\",\"id\":\"").Append(observationId).Append('"')
                  .Append(",\"status\":\"final\"")
                  .Append(",\"category\":[{\"coding\":[{\"system\":\"http://terminology.hl7.org/CodeSystem/observation-category\",\"code\":\"vital-signs\"}]}]")
                  .Append(",\"code\":{\"coding\":[{\"system\":\"http://loinc.org\",\"code\":\"").Append(ObservationCode(n)).Append("\"}]}")
                  .Append(",\"subject\":{\"reference\":\"").Append(patientRef).Append("\"}")
                  .Append(",\"encounter\":{\"reference\":\"Encounter/").Append(enc).Append("\"}")
                  .Append(",\"effectiveDateTime\":\"").Append(Timestamp(patientIndex, n)).Append('"')
                  .Append(",\"performer\":[{\"reference\":\"Practitioner/").Append(prac).Append("\"}]");

                // Leak shape 2: focus points at a DIFFERENT patient. Reached on the second hop of
                // _include=DiagnosticReport:result&_include:iterate=Observation:focus.
                if (IsCrossCompartment(n, 0))
                {
                    sb.Append(",\"focus\":[{\"reference\":\"Patient/").Append(PatientId(nextPatient)).Append("\"}]");
                }

                // Leak shape 3: device points at a Device owned by a DIFFERENT patient, which must be
                // excluded by the conditional-visibility (Device) rules.
                if (IsCrossCompartment(n, 1))
                {
                    sb.Append(",\"device\":{\"reference\":\"Device/perf-dev-").Append(nextPatient.ToString("D6", CultureInfo.InvariantCulture)).Append("-0000\"}");
                }

                sb.Append(",\"valueQuantity\":{\"value\":").Append(60 + (n % 40)).Append(",\"unit\":\"mm[Hg]\",\"system\":\"http://unitsofmeasure.org\",\"code\":\"mm[Hg]\"}}");
                _writers.Observation.WriteLine(sb.ToString());
            }

            // ── Conditions ───────────────────────────────────────────────────────────────────────
            for (int n = 0; n < _profile.ConditionsPerPatient * multiplier; n++)
            {
                string enc = $"perf-enc-{patientIndex:D6}-{n % Math.Max(encounterCount, 1):D4}";
                string prac = PractitionerId((practitionerSeed + n) % _profile.PractitionerCount);

                sb.Clear();
                sb.Append("{\"resourceType\":\"Condition\",\"id\":\"perf-con-").Append(patientIndex.ToString("D6", CultureInfo.InvariantCulture)).Append('-').Append(n.ToString("D4", CultureInfo.InvariantCulture)).Append('"')
                  .Append(",\"clinicalStatus\":{\"coding\":[{\"system\":\"http://terminology.hl7.org/CodeSystem/condition-clinical\",\"code\":\"active\"}]}")
                  .Append(",\"code\":{\"coding\":[{\"system\":\"http://snomed.info/sct\",\"code\":\"").Append(44054006 + (n % 50)).Append("\"}]}")
                  .Append(",\"subject\":{\"reference\":\"").Append(patientRef).Append("\"}")
                  .Append(",\"encounter\":{\"reference\":\"Encounter/").Append(enc).Append("\"}")
                  .Append(",\"asserter\":{\"reference\":\"Practitioner/").Append(prac).Append("\"}")
                  .Append(",\"recordedDate\":\"").Append(Timestamp(patientIndex, n)).Append("\"}");
                _writers.Condition.WriteLine(sb.ToString());
            }

            // ── MedicationRequests ───────────────────────────────────────────────────────────────
            for (int n = 0; n < _profile.MedicationRequestsPerPatient * multiplier; n++)
            {
                string enc = $"perf-enc-{patientIndex:D6}-{n % Math.Max(encounterCount, 1):D4}";
                string prac = PractitionerId((practitionerSeed + n) % _profile.PractitionerCount);
                string med = MedicationId((patientIndex + n) % _profile.MedicationCount);

                sb.Clear();
                sb.Append("{\"resourceType\":\"MedicationRequest\",\"id\":\"perf-mrq-").Append(patientIndex.ToString("D6", CultureInfo.InvariantCulture)).Append('-').Append(n.ToString("D4", CultureInfo.InvariantCulture)).Append('"')
                  .Append(",\"status\":\"active\",\"intent\":\"order\"")
                  .Append(",\"medicationReference\":{\"reference\":\"Medication/").Append(med).Append("\"}")
                  .Append(",\"subject\":{\"reference\":\"").Append(patientRef).Append("\"}")
                  .Append(",\"encounter\":{\"reference\":\"Encounter/").Append(enc).Append("\"}")
                  .Append(",\"authoredOn\":\"").Append(Timestamp(patientIndex, n)).Append('"')
                  .Append(",\"requester\":{\"reference\":\"Practitioner/").Append(prac).Append("\"}}");
                _writers.MedicationRequest.WriteLine(sb.ToString());
            }

            // ── DiagnosticReports (multi-result: the _include:iterate driver) ────────────────────
            for (int n = 0; n < _profile.DiagnosticReportsPerPatient * multiplier; n++)
            {
                string enc = $"perf-enc-{patientIndex:D6}-{n % Math.Max(encounterCount, 1):D4}";
                string org = OrganizationId((organizationSeed + n) % _profile.OrganizationCount);

                sb.Clear();
                sb.Append("{\"resourceType\":\"DiagnosticReport\",\"id\":\"perf-dgr-").Append(patientIndex.ToString("D6", CultureInfo.InvariantCulture)).Append('-').Append(n.ToString("D4", CultureInfo.InvariantCulture)).Append('"')
                  .Append(",\"status\":\"final\"")
                  .Append(",\"code\":{\"coding\":[{\"system\":\"http://loinc.org\",\"code\":\"58410-2\"}]}")
                  .Append(",\"subject\":{\"reference\":\"").Append(patientRef).Append("\"}")
                  .Append(",\"encounter\":{\"reference\":\"Encounter/").Append(enc).Append("\"}")
                  .Append(",\"effectiveDateTime\":\"").Append(Timestamp(patientIndex, n)).Append('"')
                  .Append(",\"performer\":[{\"reference\":\"Organization/").Append(org).Append("\"}]")
                  .Append(",\"result\":[");

                for (int r = 0; r < _profile.ResultsPerDiagnosticReport; r++)
                {
                    if (r > 0)
                    {
                        sb.Append(',');
                    }

                    // Leak shape 1: the last result of a share of reports points at the PREVIOUS patient's
                    // Observation. From that patient's compartment the report is reachable through
                    // _revinclude=Observation:subject&_revinclude:iterate=DiagnosticReport:result but is
                    // itself out of compartment, so it must not be disclosed.
                    bool crossCompartment = r == _profile.ResultsPerDiagnosticReport - 1 && IsCrossCompartment(n, 2);
                    int ownerIndex = crossCompartment ? previousPatient : patientIndex;
                    int obsIndex = crossCompartment
                        ? ((n * 7) + r) % _profile.ObservationsPerPatient
                        : ((n * _profile.ResultsPerDiagnosticReport) + r) % Math.Max(observationCount, 1);

                    sb.Append("{\"reference\":\"Observation/").Append(ObservationId(ownerIndex, obsIndex)).Append("\"}");
                }

                sb.Append("]}");
                _writers.DiagnosticReport.WriteLine(sb.ToString());
            }

            // ── DocumentReferences ───────────────────────────────────────────────────────────────
            for (int n = 0; n < _profile.DocumentReferencesPerPatient * multiplier; n++)
            {
                string enc = $"perf-enc-{patientIndex:D6}-{n % Math.Max(encounterCount, 1):D4}";
                string prac = PractitionerId((practitionerSeed + n) % _profile.PractitionerCount);
                string org = OrganizationId((organizationSeed + n) % _profile.OrganizationCount);

                sb.Clear();
                sb.Append("{\"resourceType\":\"DocumentReference\",\"id\":\"perf-doc-").Append(patientIndex.ToString("D6", CultureInfo.InvariantCulture)).Append('-').Append(n.ToString("D4", CultureInfo.InvariantCulture)).Append('"')
                  .Append(",\"status\":\"current\"")
                  .Append(",\"type\":{\"coding\":[{\"system\":\"http://loinc.org\",\"code\":\"34133-9\"}]}")
                  .Append(",\"subject\":{\"reference\":\"").Append(patientRef).Append("\"}")
                  .Append(",\"date\":\"").Append(Timestamp(patientIndex, n)).Append('"')
                  .Append(",\"author\":[{\"reference\":\"Practitioner/").Append(prac).Append("\"}]")
                  .Append(",\"custodian\":{\"reference\":\"Organization/").Append(org).Append("\"}")
                  .Append(",\"content\":[{\"attachment\":{\"contentType\":\"text/plain\",\"title\":\"perf-note\"}}]")
                  .Append(",\"context\":{\"encounter\":[{\"reference\":\"Encounter/").Append(enc).Append("\"}]}}");
                _writers.DocumentReference.WriteLine(sb.ToString());
            }

            // ── Procedures ───────────────────────────────────────────────────────────────────────
            for (int n = 0; n < _profile.ProceduresPerPatient * multiplier; n++)
            {
                string enc = $"perf-enc-{patientIndex:D6}-{n % Math.Max(encounterCount, 1):D4}";
                string prac = PractitionerId((practitionerSeed + n) % _profile.PractitionerCount);

                sb.Clear();
                sb.Append("{\"resourceType\":\"Procedure\",\"id\":\"perf-prc-").Append(patientIndex.ToString("D6", CultureInfo.InvariantCulture)).Append('-').Append(n.ToString("D4", CultureInfo.InvariantCulture)).Append('"')
                  .Append(",\"status\":\"completed\"")
                  .Append(",\"code\":{\"coding\":[{\"system\":\"http://snomed.info/sct\",\"code\":\"").Append(80146002 + (n % 30)).Append("\"}]}")
                  .Append(",\"subject\":{\"reference\":\"").Append(patientRef).Append("\"}")
                  .Append(",\"encounter\":{\"reference\":\"Encounter/").Append(enc).Append("\"}")
                  .Append(",\"performedDateTime\":\"").Append(Timestamp(patientIndex, n)).Append('"')
                  .Append(",\"performer\":[{\"actor\":{\"reference\":\"Practitioner/").Append(prac).Append("\"}}]}");
                _writers.Procedure.WriteLine(sb.ToString());
            }

            // ── AllergyIntolerances ──────────────────────────────────────────────────────────────
            for (int n = 0; n < _profile.AllergiesPerPatient * multiplier; n++)
            {
                string prac = PractitionerId((practitionerSeed + n) % _profile.PractitionerCount);

                sb.Clear();
                sb.Append("{\"resourceType\":\"AllergyIntolerance\",\"id\":\"perf-alg-").Append(patientIndex.ToString("D6", CultureInfo.InvariantCulture)).Append('-').Append(n.ToString("D4", CultureInfo.InvariantCulture)).Append('"')
                  .Append(",\"clinicalStatus\":{\"coding\":[{\"system\":\"http://terminology.hl7.org/CodeSystem/allergyintolerance-clinical\",\"code\":\"active\"}]}")
                  .Append(",\"code\":{\"coding\":[{\"system\":\"http://snomed.info/sct\",\"code\":\"").Append(91935009 + (n % 20)).Append("\"}]}")
                  .Append(",\"patient\":{\"reference\":\"").Append(patientRef).Append("\"}")
                  .Append(",\"recordedDate\":\"").Append(Timestamp(patientIndex, n)).Append('"')
                  .Append(",\"recorder\":{\"reference\":\"Practitioner/").Append(prac).Append("\"}}");
                _writers.AllergyIntolerance.WriteLine(sb.ToString());
            }

            // ── Immunizations ────────────────────────────────────────────────────────────────────
            for (int n = 0; n < _profile.ImmunizationsPerPatient * multiplier; n++)
            {
                string enc = $"perf-enc-{patientIndex:D6}-{n % Math.Max(encounterCount, 1):D4}";
                string prac = PractitionerId((practitionerSeed + n) % _profile.PractitionerCount);
                string loc = LocationId((patientIndex + n) % _profile.LocationCount);

                sb.Clear();
                sb.Append("{\"resourceType\":\"Immunization\",\"id\":\"perf-imm-").Append(patientIndex.ToString("D6", CultureInfo.InvariantCulture)).Append('-').Append(n.ToString("D4", CultureInfo.InvariantCulture)).Append('"')
                  .Append(",\"status\":\"completed\"")
                  .Append(",\"vaccineCode\":{\"coding\":[{\"system\":\"http://hl7.org/fhir/sid/cvx\",\"code\":\"").Append(140 + (n % 20)).Append("\"}]}")
                  .Append(",\"patient\":{\"reference\":\"").Append(patientRef).Append("\"}")
                  .Append(",\"occurrenceDateTime\":\"").Append(Timestamp(patientIndex, n)).Append('"')
                  .Append(",\"encounter\":{\"reference\":\"Encounter/").Append(enc).Append("\"}")
                  .Append(",\"location\":{\"reference\":\"Location/").Append(loc).Append("\"}")
                  .Append(",\"performer\":[{\"actor\":{\"reference\":\"Practitioner/").Append(prac).Append("\"}}]}");
                _writers.Immunization.WriteLine(sb.ToString());
            }

            // ── CarePlans ────────────────────────────────────────────────────────────────────────
            for (int n = 0; n < _profile.CarePlansPerPatient * multiplier; n++)
            {
                string enc = $"perf-enc-{patientIndex:D6}-{n % Math.Max(encounterCount, 1):D4}";
                string prac = PractitionerId((practitionerSeed + n) % _profile.PractitionerCount);

                sb.Clear();
                sb.Append("{\"resourceType\":\"CarePlan\",\"id\":\"perf-cpl-").Append(patientIndex.ToString("D6", CultureInfo.InvariantCulture)).Append('-').Append(n.ToString("D4", CultureInfo.InvariantCulture)).Append('"')
                  .Append(",\"status\":\"active\",\"intent\":\"plan\"")
                  .Append(",\"subject\":{\"reference\":\"").Append(patientRef).Append("\"}")
                  .Append(",\"encounter\":{\"reference\":\"Encounter/").Append(enc).Append("\"}")
                  .Append(",\"period\":{\"start\":\"").Append(Timestamp(patientIndex, n)).Append("\"}")
                  .Append(",\"author\":{\"reference\":\"Practitioner/").Append(prac).Append("\"}}");
                _writers.CarePlan.WriteLine(sb.ToString());
            }

            // ── Devices ──────────────────────────────────────────────────────────────────────────
            // A configurable share carry Device.patient; the rest are unassigned. Both shapes are needed to
            // exercise the SMART conditional-visibility rules (EXISTS own-device / NOT EXISTS unassigned).
            for (int n = 0; n < _profile.DevicesPerPatient * multiplier; n++)
            {
                bool assigned = ((patientIndex + n) % 100) < _profile.DeviceWithPatientPercent;

                sb.Clear();
                sb.Append("{\"resourceType\":\"Device\",\"id\":\"perf-dev-").Append(patientIndex.ToString("D6", CultureInfo.InvariantCulture)).Append('-').Append(n.ToString("D4", CultureInfo.InvariantCulture)).Append('"')
                  .Append(",\"status\":\"active\"")
                  .Append(",\"deviceName\":[{\"name\":\"Perf Monitor\",\"type\":\"model-name\"}]")
                  .Append(",\"type\":{\"coding\":[{\"system\":\"http://snomed.info/sct\",\"code\":\"86184003\"}]}");

                if (assigned)
                {
                    sb.Append(",\"patient\":{\"reference\":\"").Append(patientRef).Append("\"}");
                }

                sb.Append('}');
                _writers.Device.WriteLine(sb.ToString());
            }
        }

        /// <summary>
        /// Writes the shared "universal" resources (Practitioner, Organization, Location, Medication).
        /// These are visible in every SMART compartment, so they are the cheap leg of the authorization
        /// predicate and must be present in realistic volume.
        /// </summary>
        internal void WriteSharedResources()
        {
            var sb = new StringBuilder(512);

            for (int i = 0; i < _profile.PractitionerCount; i++)
            {
                sb.Clear();
                sb.Append("{\"resourceType\":\"Practitioner\",\"id\":\"").Append(PractitionerId(i)).Append('"')
                  .Append(",\"active\":true,\"name\":[{\"family\":\"Provider").Append(i).Append("\",\"given\":[\"Perf\"],\"prefix\":[\"Dr\"]}]")
                  .Append(",\"gender\":\"").Append((i % 2) == 0 ? "female" : "male").Append("\"}");
                _writers.Practitioner.WriteLine(sb.ToString());
            }

            for (int i = 0; i < _profile.OrganizationCount; i++)
            {
                sb.Clear();
                sb.Append("{\"resourceType\":\"Organization\",\"id\":\"").Append(OrganizationId(i)).Append('"')
                  .Append(",\"active\":true,\"name\":\"Perf Clinic ").Append(i).Append("\"}");
                _writers.Organization.WriteLine(sb.ToString());
            }

            for (int i = 0; i < _profile.LocationCount; i++)
            {
                sb.Clear();
                sb.Append("{\"resourceType\":\"Location\",\"id\":\"").Append(LocationId(i)).Append('"')
                  .Append(",\"status\":\"active\",\"name\":\"Perf Site ").Append(i).Append("\"}");
                _writers.Location.WriteLine(sb.ToString());
            }

            for (int i = 0; i < _profile.MedicationCount; i++)
            {
                sb.Clear();
                sb.Append("{\"resourceType\":\"Medication\",\"id\":\"").Append(MedicationId(i)).Append('"')
                  .Append(",\"status\":\"active\"")
                  .Append(",\"code\":{\"coding\":[{\"system\":\"http://www.nlm.nih.gov/research/umls/rxnorm\",\"code\":\"").Append(1000000 + i).Append("\"}]}}");
                _writers.Medication.WriteLine(sb.ToString());
            }
        }

        private static string ObservationId(int patientIndex, int n) =>
            $"perf-obs-{patientIndex:D6}-{n:D5}";

        /// <summary>
        /// Deterministically selects a share of resources to carry a cross-compartment reference.
        /// The offset lets several independent leak shapes be spread across different resources instead of
        /// all landing on the same ones.
        /// </summary>
        private bool IsCrossCompartment(int n, int offset) =>
            _profile.CrossCompartmentPercent > 0 &&
            (((n * 3) + (offset * 31)) % 100) < _profile.CrossCompartmentPercent;

        private static string ObservationCode(int n) => (n % 5) switch
        {
            0 => "8867-4",
            1 => "8480-6",
            2 => "8462-4",
            3 => "9279-1",
            _ => "2708-6",
        };

        private static string BirthDate(int patientIndex)
        {
            int year = 1940 + (patientIndex % 70);
            int month = 1 + (patientIndex % 12);
            int day = 1 + (patientIndex % 28);
            return $"{year:D4}-{month:D2}-{day:D2}";
        }

        private static string Timestamp(int patientIndex, int n)
        {
            int year = 2015 + ((patientIndex + n) % 10);
            int month = 1 + ((patientIndex + n) % 12);
            int day = 1 + ((patientIndex + (n * 3)) % 28);
            int hour = (patientIndex + n) % 24;
            int minute = (n * 7) % 60;
            return $"{year:D4}-{month:D2}-{day:D2}T{hour:D2}:{minute:D2}:00Z";
        }
    }
}
