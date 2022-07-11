// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Persistence
{
    public class ResourceKey : IEquatable<ResourceKey>
    {
        private static Dictionary<string, string> _nameToId;

#pragma warning disable CA1810 // Initialize reference type static fields inline
        static ResourceKey()
#pragma warning restore CA1810 // Initialize reference type static fields inline
        {
            _nameToId = new Dictionary<string, string>();
            _nameToId.Add("Account", "1");
            _nameToId.Add("ActivityDefinition", "2");
            _nameToId.Add("AdverseEvent", "3");
            _nameToId.Add("AllergyIntolerance", "4");
            _nameToId.Add("Appointment", "5");
            _nameToId.Add("AppointmentResponse", "6");
            _nameToId.Add("AuditEvent", "7");
            _nameToId.Add("Basic", "8");
            _nameToId.Add("Binary", "9");
            _nameToId.Add("BiologicallyDerivedProduct", "10");
            _nameToId.Add("BodyStructure", "11");
            _nameToId.Add("Bundle", "12");
            _nameToId.Add("CapabilityStatement", "13");
            _nameToId.Add("CarePlan", "14");
            _nameToId.Add("CareTeam", "15");
            _nameToId.Add("CatalogEntry", "16");
            _nameToId.Add("ChargeItem", "17");
            _nameToId.Add("ChargeItemDefinition", "18");
            _nameToId.Add("Claim", "19");
            _nameToId.Add("ClaimResponse", "20");
            _nameToId.Add("ClinicalImpression", "21");
            _nameToId.Add("CodeSystem", "22");
            _nameToId.Add("Communication", "23");
            _nameToId.Add("CommunicationRequest", "24");
            _nameToId.Add("CompartmentDefinition", "25");
            _nameToId.Add("Composition", "26");
            _nameToId.Add("ConceptMap", "27");
            _nameToId.Add("Condition", "28");
            _nameToId.Add("Consent", "29");
            _nameToId.Add("Contract", "30");
            _nameToId.Add("Coverage", "31");
            _nameToId.Add("CoverageEligibilityRequest", "32");
            _nameToId.Add("CoverageEligibilityResponse", "33");
            _nameToId.Add("DetectedIssue", "34");
            _nameToId.Add("Device", "35");
            _nameToId.Add("DeviceDefinition", "36");
            _nameToId.Add("DeviceMetric", "37");
            _nameToId.Add("DeviceRequest", "38");
            _nameToId.Add("DeviceUseStatement", "39");
            _nameToId.Add("DiagnosticReport", "40");
            _nameToId.Add("DocumentManifest", "41");
            _nameToId.Add("DocumentReference", "42");
            _nameToId.Add("EffectEvidenceSynthesis", "43");
            _nameToId.Add("Encounter", "44");
            _nameToId.Add("Endpoint", "45");
            _nameToId.Add("EnrollmentRequest", "46");
            _nameToId.Add("EnrollmentResponse", "47");
            _nameToId.Add("EpisodeOfCare", "48");
            _nameToId.Add("EventDefinition", "49");
            _nameToId.Add("Evidence", "50");
            _nameToId.Add("EvidenceVariable", "51");
            _nameToId.Add("ExampleScenario", "52");
            _nameToId.Add("ExplanationOfBenefit", "53");
            _nameToId.Add("FamilyMemberHistory", "54");
            _nameToId.Add("Flag", "55");
            _nameToId.Add("Goal", "56");
            _nameToId.Add("GraphDefinition", "57");
            _nameToId.Add("Group", "58");
            _nameToId.Add("GuidanceResponse", "59");
            _nameToId.Add("HealthcareService", "60");
            _nameToId.Add("ImagingStudy", "61");
            _nameToId.Add("Immunization", "62");
            _nameToId.Add("ImmunizationEvaluation", "63");
            _nameToId.Add("ImmunizationRecommendation", "64");
            _nameToId.Add("ImplementationGuide", "65");
            _nameToId.Add("InsurancePlan", "66");
            _nameToId.Add("Invoice", "67");
            _nameToId.Add("Library", "68");
            _nameToId.Add("Linkage", "69");
            _nameToId.Add("List", "70");
            _nameToId.Add("Location", "71");
            _nameToId.Add("Measure", "72");
            _nameToId.Add("MeasureReport", "73");
            _nameToId.Add("Media", "74");
            _nameToId.Add("Medication", "75");
            _nameToId.Add("MedicationAdministration", "76");
            _nameToId.Add("MedicationDispense", "77");
            _nameToId.Add("MedicationKnowledge", "78");
            _nameToId.Add("MedicationRequest", "79");
            _nameToId.Add("MedicationStatement", "80");
            _nameToId.Add("MedicinalProduct", "81");
            _nameToId.Add("MedicinalProductAuthorization", "82");
            _nameToId.Add("MedicinalProductContraindication", "83");
            _nameToId.Add("MedicinalProductIndication", "84");
            _nameToId.Add("MedicinalProductIngredient", "85");
            _nameToId.Add("MedicinalProductInteraction", "86");
            _nameToId.Add("MedicinalProductManufactured", "87");
            _nameToId.Add("MedicinalProductPackaged", "88");
            _nameToId.Add("MedicinalProductPharmaceutical", "89");
            _nameToId.Add("MedicinalProductUndesirableEffect", "90");
            _nameToId.Add("MessageDefinition", "91");
            _nameToId.Add("MessageHeader", "92");
            _nameToId.Add("MolecularSequence", "93");
            _nameToId.Add("NamingSystem", "94");
            _nameToId.Add("NutritionOrder", "95");
            _nameToId.Add("Observation", "96");
            _nameToId.Add("ObservationDefinition", "97");
            _nameToId.Add("OperationDefinition", "98");
            _nameToId.Add("OperationOutcome", "99");
            _nameToId.Add("Organization", "100");
            _nameToId.Add("OrganizationAffiliation", "101");
            _nameToId.Add("Parameters", "102");
            _nameToId.Add("Patient", "103");
            _nameToId.Add("PaymentNotice", "104");
            _nameToId.Add("PaymentReconciliation", "105");
            _nameToId.Add("Person", "106");
            _nameToId.Add("PlanDefinition", "107");
            _nameToId.Add("Practitioner", "108");
            _nameToId.Add("PractitionerRole", "109");
            _nameToId.Add("Procedure", "110");
            _nameToId.Add("Provenance", "111");
            _nameToId.Add("Questionnaire", "112");
            _nameToId.Add("QuestionnaireResponse", "113");
            _nameToId.Add("RelatedPerson", "114");
            _nameToId.Add("RequestGroup", "115");
            _nameToId.Add("ResearchDefinition", "116");
            _nameToId.Add("ResearchElementDefinition", "117");
            _nameToId.Add("ResearchStudy", "118");
            _nameToId.Add("ResearchSubject", "119");
            _nameToId.Add("RiskAssessment", "120");
            _nameToId.Add("RiskEvidenceSynthesis", "121");
            _nameToId.Add("Schedule", "122");
            _nameToId.Add("SearchParameter", "123");
            _nameToId.Add("ServiceRequest", "124");
            _nameToId.Add("Slot", "125");
            _nameToId.Add("Specimen", "126");
            _nameToId.Add("SpecimenDefinition", "127");
            _nameToId.Add("StructureDefinition", "128");
            _nameToId.Add("StructureMap", "129");
            _nameToId.Add("Subscription", "130");
            _nameToId.Add("Substance", "131");
            _nameToId.Add("SubstanceNucleicAcid", "132");
            _nameToId.Add("SubstancePolymer", "133");
            _nameToId.Add("SubstanceProtein", "134");
            _nameToId.Add("SubstanceReferenceInformation", "135");
            _nameToId.Add("SubstanceSourceMaterial", "136");
            _nameToId.Add("SubstanceSpecification", "137");
            _nameToId.Add("SupplyDelivery", "138");
            _nameToId.Add("SupplyRequest", "139");
            _nameToId.Add("Task", "140");
            _nameToId.Add("TerminologyCapabilities", "141");
            _nameToId.Add("TestReport", "142");
            _nameToId.Add("TestScript", "143");
            _nameToId.Add("ValueSet", "144");
            _nameToId.Add("VerificationResult", "145");
            _nameToId.Add("VisionPrescription", "146");
        }

        public ResourceKey(string resourceType, string id, string versionId = null)
        {
            EnsureArg.IsNotNullOrEmpty(resourceType, nameof(resourceType));
            EnsureArg.IsNotNullOrEmpty(id, nameof(id));
            EnsureArg.IsTrue(ModelInfoProvider.IsKnownResource(resourceType), nameof(resourceType));

            Id = id;
            VersionId = versionId;
            ResourceType = resourceType;
        }

        public string Id { get; }

        public string VersionId { get; }

        public string ResourceType { get; }

        public bool Equals(ResourceKey other)
        {
            if (ReferenceEquals(null, other))
            {
                return false;
            }

            if (ReferenceEquals(this, other))
            {
                return true;
            }

            return Id == other.Id &&
                   VersionId == other.VersionId &&
                   ResourceType == other.ResourceType;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
            {
                return false;
            }

            if (ReferenceEquals(this, obj))
            {
                return true;
            }

            if (obj.GetType() != this.GetType())
            {
                return false;
            }

            return Equals((ResourceKey)obj);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Id, VersionId, ResourceType);
        }

        public static string NameToId(string name)
        {
            return _nameToId.TryGetValue(name, out var id) ? id : name;
        }
    }
}
