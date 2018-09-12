// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Text.RegularExpressions;
using EnsureThat;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Context;

namespace Microsoft.Health.Fhir.Core.Features.Search.SearchValues
{
    /// <summary>
    /// Provides mechanism to parse a string to an instance of <see cref="ReferenceSearchValue"/>.
    /// </summary>
    public class ReferenceSearchValueParser : IReferenceSearchValueParser
    {
        private const string BaseUriCapture = "baseUri";
        private const string ResourceTypeCapture = "resourceType";
        private const string ResourceIdCapture = "resourceId";

        // The following regular expression is captured from the spec: http://hl7.org/fhir/STU3/references.html#literal
        // with a few modifications.
        // 1. Explicit capture is added.
        // 2. Ignore case is added to http and https since scheme is case-incensitive.
        // 3. Allowed the URL to contain //.
        private static readonly Regex ReferenceRegex = new Regex(
            $@"((?<{BaseUriCapture}>((?i)http|(?i)https)://([A-Za-z0-9\\\.\:\%\$]*\/)*))?(?<{ResourceTypeCapture}>Account|ActivityDefinition|AdverseEvent|AllergyIntolerance|Appointment|AppointmentResponse|AuditEvent|Basic|Binary|BodySite|Bundle|CapabilityStatement|CarePlan|CareTeam|ChargeItem|Claim|ClaimResponse|ClinicalImpression|CodeSystem|Communication|CommunicationRequest|CompartmentDefinition|Composition|ConceptMap|Condition|Consent|Contract|Coverage|DataElement|DetectedIssue|Device|DeviceComponent|DeviceMetric|DeviceRequest|DeviceUseStatement|DiagnosticReport|DocumentManifest|DocumentReference|EligibilityRequest|EligibilityResponse|Encounter|Endpoint|EnrollmentRequest|EnrollmentResponse|EpisodeOfCare|ExpansionProfile|ExplanationOfBenefit|FamilyMemberHistory|Flag|Goal|GraphDefinition|Group|GuidanceResponse|HealthcareService|ImagingManifest|ImagingStudy|Immunization|ImmunizationRecommendation|ImplementationGuide|Library|Linkage|List|Location|Measure|MeasureReport|Media|Medication|MedicationAdministration|MedicationDispense|MedicationRequest|MedicationStatement|MessageDefinition|MessageHeader|NamingSystem|NutritionOrder|Observation|OperationDefinition|OperationOutcome|Organization|Patient|PaymentNotice|PaymentReconciliation|Person|PlanDefinition|Practitioner|PractitionerRole|Procedure|ProcedureRequest|ProcessRequest|ProcessResponse|Provenance|Questionnaire|QuestionnaireResponse|ReferralRequest|RelatedPerson|RequestGroup|ResearchStudy|ResearchSubject|RiskAssessment|Schedule|SearchParameter|Sequence|ServiceDefinition|Slot|Specimen|StructureDefinition|StructureMap|Subscription|Substance|SupplyDelivery|SupplyRequest|Task|TestReport|TestScript|ValueSet|VisionPrescription)\/(?<{ResourceIdCapture}>[A-Za-z0-9\-\.]{{1,64}})(\/_history\/[A-Za-z0-9\-\.]{{1,64}})?",
            RegexOptions.Singleline | RegexOptions.Compiled | RegexOptions.ExplicitCapture);

        private readonly IFhirContextAccessor _fhirContextAccessor;

        public ReferenceSearchValueParser(IFhirContextAccessor fhirContextAccessor)
        {
            EnsureArg.IsNotNull(fhirContextAccessor, nameof(fhirContextAccessor));

            _fhirContextAccessor = fhirContextAccessor;
        }

        /// <inheritdoc />
        public ReferenceSearchValue Parse(string s)
        {
            EnsureArg.IsNotNullOrWhiteSpace(s, nameof(s));

            Match match = ReferenceRegex.Match(s);

            if (match.Success)
            {
                Uri baseUri = null;

                if (match.Groups[BaseUriCapture].Success)
                {
                    // The reference starts with http or https and therefore we can capture the base URI.
                    baseUri = new Uri(match.Groups[BaseUriCapture].Value);
                }

                string resourceTypeInString = match.Groups[ResourceTypeCapture].Value;
                ResourceType resourceType = Enum.Parse<ResourceType>(resourceTypeInString);

                string resourceId = match.Groups[ResourceIdCapture].Value;

                if (baseUri == _fhirContextAccessor.FhirContext.BaseUri)
                {
                    // This is an absolute URL pointing to an internal resource.
                    return new ReferenceSearchValue(
                        ReferenceKind.Internal,
                        null,
                        resourceType,
                        resourceId);
                }
                else if (baseUri != null)
                {
                    // This is an absolute URL pointing to an external resource.
                    return new ReferenceSearchValue(
                        ReferenceKind.External,
                        baseUri,
                        resourceType,
                        resourceId);
                }
                else if (baseUri == null)
                {
                    if (s.StartsWith(resourceTypeInString, StringComparison.Ordinal))
                    {
                        // This is relative URL.
                        return new ReferenceSearchValue(
                            ReferenceKind.InternalOrExternal,
                            null,
                            resourceType,
                            resourceId);
                    }
                }
            }

            return new ReferenceSearchValue(
                ReferenceKind.InternalOrExternal,
                baseUri: null,
                resourceType: null,
                resourceId: s);
        }
    }
}
