﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Health.Fhir.Tests.Common;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Health.Fhir.Tests.E2E.Crucible
{
    public class CrucibleTestFixture : IClassFixture<CrucibleDataSource>
    {
        private static readonly string[] KnownFailures =
        {
            "argonautproviderconnectathontest/APCT03",
            "argonautproviderconnectathontest/APCT04",
            "argonautproviderconnectathontest/APCT05",
            "argonautproviderconnectathontest/APCT06",
            "connectathon_patient_track/C8T1_3",
            "connectathon-15-location-locate-endpoint-json/01-LocationSearchName",
            "connectathon-15-location-locate-endpoint-json/02-LocationSearchAddress",
            "connectathon-15-location-locate-endpoint-json/03-LocationSearchAddressCityStatePostalCode",
            "connectathon-15-location-locate-endpoint-xml/01-LocationSearchName",
            "connectathon-15-location-locate-endpoint-xml/02-LocationSearchAddress",
            "connectathon-15-location-locate-endpoint-xml/03-LocationSearchAddressCityStatePostalCode",
            "connectathon-15-location-locate-telecom-json/01-LocationSearchName",
            "connectathon-15-location-locate-telecom-json/02-LocationSearchAddress",
            "connectathon-15-location-locate-telecom-json/03-LocationSearchAddressCityStatePostalCode",
            "connectathon-15-location-locate-telecom-xml/01-LocationSearchName",
            "connectathon-15-location-locate-telecom-xml/02-LocationSearchAddress",
            "connectathon-15-location-locate-telecom-xml/03-LocationSearchAddressCityStatePostalCode",
            "connectathon-15-organizational-relationships-json/01-PractitionerRoleSearchOrganizationReference",
            "connectathon-15-organizational-relationships-json/02-PractitionerRoleSearchOrganizationAddress",
            "connectathon-15-organizational-relationships-json/03-PractitionerRoleSearchOrganizationName",
            "connectathon-15-organizational-relationships-xml/01-PractitionerRoleSearchOrganizationReference",
            "connectathon-15-organizational-relationships-xml/02-PractitionerRoleSearchOrganizationAddress",
            "connectathon-15-organizational-relationships-xml/03-PractitionerRoleSearchOrganizationName",
            "connectathon-15-organization-locate-endpoint-json/01-OrganizationSearchIdentifier",
            "connectathon-15-organization-locate-endpoint-json/02-OrganizationSearchName",
            "connectathon-15-organization-locate-endpoint-xml/01-OrganizationSearchIdentifier",
            "connectathon-15-organization-locate-endpoint-xml/02-OrganizationSearchName",
            "connectathon-15-patient-base-client-id-json/Step2-UpdatePatient",
            "connectathon-15-patient-base-client-id-json/Step4-PatientSearch",
            "connectathon-15-patient-base-client-id-xml/Step2-UpdatePatient",
            "connectathon-15-patient-base-client-id-xml/Step4-PatientSearch",
            "connectathon-15-patient-base-server-id-json/Step1-RegisterNewPatient",
            "connectathon-15-patient-base-server-id-json/Step2-UpdatePatient",
            "connectathon-15-patient-base-server-id-json/Step4-PatientSearch",
            "connectathon-15-patient-base-server-id-xml/Step1-RegisterNewPatient",
            "connectathon-15-patient-base-server-id-xml/Step2-UpdatePatient",
            "connectathon-15-patient-base-server-id-xml/Step4-PatientSearch",
            "connectathon-15-patient-bonus-client-id-json/Step2a-Bonus1-UpdatePatient",
            "connectathon-15-patient-bonus-client-id-json/Step2b-Bonus2-UpdatePatient",
            "connectathon-15-patient-bonus-client-id-json/Step4-PatientSearch",
            "connectathon-15-patient-bonus-client-id-xml/Step2a-Bonus1-UpdatePatient",
            "connectathon-15-patient-bonus-client-id-xml/Step2b-Bonus2-UpdatePatient",
            "connectathon-15-patient-bonus-client-id-xml/Step4-PatientSearch",
            "connectathon-15-patient-bonus-server-id-json/Step1-RegisterNewPatient",
            "connectathon-15-patient-bonus-server-id-json/Step2a-Bonus1-UpdatePatient",
            "connectathon-15-patient-bonus-server-id-json/Step2b-Bonus2-UpdatePatient",
            "connectathon-15-patient-bonus-server-id-json/Step4-PatientSearch",
            "connectathon-15-patient-bonus-server-id-xml/Step1-RegisterNewPatient",
            "connectathon-15-patient-bonus-server-id-xml/Step2a-Bonus1-UpdatePatient",
            "connectathon-15-patient-bonus-server-id-xml/Step2b-Bonus2-UpdatePatient",
            "connectathon-15-patient-bonus-server-id-xml/Step4-PatientSearch",
            "connectathon-15-patient-fhirclient-01-register-server-id-json/01-RegisterNewPatient",
            "connectathon-15-patient-fhirclient-01-register-server-id-xml/01-RegisterNewPatient",
            "connectathon-15-patient-fhirclient-02-update-json/01-RegisterNewPatient",
            "connectathon-15-patient-fhirclient-02-update-xml/01-RegisterNewPatient",
            "connectathon-15-patient-fhirclient-03-read-json/01-ReadPatient",
            "connectathon-15-patient-fhirclient-03-read-xml/01-ReadPatient",
            "connectathon-15-patient-fhirclient-05-vread-json/01-VersionReadPatient",
            "connectathon-15-patient-fhirclient-05-vread-xml/01-VersionReadPatient",
            "connectathon-15-patient-fhirclient-06-search-json/01-PatientSearch",
            "connectathon-15-patient-fhirclient-06-search-xml/01-PatientSearch",
            "connectathon-15-patient-fhirclient-07-delete-json/01-DeletePatient",
            "connectathon-15-patient-fhirclient-07-delete-xml/01-DeletePatient",
            "connectathon-15-patient-fhirserver-01-register-server-id-json/01-RegisterNewPatient",
            "connectathon-15-patient-fhirserver-01-register-server-id-xml/01-RegisterNewPatient",
            "connectathon-15-patient-fhirserver-02-update-client-id-json/01-UpdatePatient",
            "connectathon-15-patient-fhirserver-02-update-client-id-json/SETUP",
            "connectathon-15-patient-fhirserver-02-update-client-id-xml/01-UpdatePatient",
            "connectathon-15-patient-fhirserver-02-update-client-id-xml/SETUP",
            "connectathon-15-patient-fhirserver-02-update-server-id-json/01-UpdatePatient",
            "connectathon-15-patient-fhirserver-02-update-server-id-xml/01-UpdatePatient",
            "connectathon-15-patient-fhirserver-03-read-client-id-json/01-ReadPatient",
            "connectathon-15-patient-fhirserver-03-read-client-id-json/SETUP",
            "connectathon-15-patient-fhirserver-03-read-client-id-xml/01-ReadPatient",
            "connectathon-15-patient-fhirserver-03-read-client-id-xml/SETUP",
            "connectathon-15-patient-fhirserver-03-read-server-id-json/01-ReadPatient",
            "connectathon-15-patient-fhirserver-03-read-server-id-xml/01-ReadPatient",
            "connectathon-15-patient-fhirserver-05-vread-client-id-json/01-VersionReadCreatedPatient",
            "connectathon-15-patient-fhirserver-05-vread-client-id-json/02-VersionReadUpdatedPatient",
            "connectathon-15-patient-fhirserver-05-vread-client-id-json/SETUP",
            "connectathon-15-patient-fhirserver-05-vread-client-id-xml/01-VersionReadCreatedPatient",
            "connectathon-15-patient-fhirserver-05-vread-client-id-xml/02-VersionReadUpdatedPatient",
            "connectathon-15-patient-fhirserver-05-vread-client-id-xml/SETUP",
            "connectathon-15-patient-fhirserver-05-vread-server-id-json/01-VersionReadCreatedPatient",
            "connectathon-15-patient-fhirserver-05-vread-server-id-json/02-VersionReadUpdatedPatient",
            "connectathon-15-patient-fhirserver-05-vread-server-id-json/SETUP",
            "connectathon-15-patient-fhirserver-05-vread-server-id-xml/01-VersionReadCreatedPatient",
            "connectathon-15-patient-fhirserver-05-vread-server-id-xml/02-VersionReadUpdatedPatient",
            "connectathon-15-patient-fhirserver-05-vread-server-id-xml/SETUP",
            "connectathon-15-patient-fhirserver-06-search-client-id-json/01-SearchPatient",
            "connectathon-15-patient-fhirserver-06-search-client-id-json/SETUP",
            "connectathon-15-patient-fhirserver-06-search-client-id-xml/01-SearchPatient",
            "connectathon-15-patient-fhirserver-06-search-client-id-xml/SETUP",
            "connectathon-15-patient-fhirserver-06-search-server-id-json/01-SearchPatient",
            "connectathon-15-patient-fhirserver-06-search-server-id-xml/01-SearchPatient",
            "connectathon-15-patient-fhirserver-07-delete-client-id-json/01-DeletePatient",
            "connectathon-15-patient-fhirserver-07-delete-client-id-json/SETUP",
            "connectathon-15-patient-fhirserver-07-delete-client-id-xml/01-DeletePatient",
            "connectathon-15-patient-fhirserver-07-delete-client-id-xml/SETUP",
            "connectathon-15-patient-fhirserver-07-delete-server-id-json/01-DeletePatient",
            "connectathon-15-patient-fhirserver-07-delete-server-id-xml/01-DeletePatient",
            "connectathon-15-patient-fhirserver-99-all-client-id-json/01-RegisterNewPatient",
            "connectathon-15-patient-fhirserver-99-all-client-id-json/02-UpdatePatient",
            "connectathon-15-patient-fhirserver-99-all-client-id-json/03-ReadPatient",
            "connectathon-15-patient-fhirserver-99-all-client-id-json/05-VersionReadPatient",
            "connectathon-15-patient-fhirserver-99-all-client-id-json/06-SearchPatient",
            "connectathon-15-patient-fhirserver-99-all-client-id-json/07-PatientDelete",
            "connectathon-15-patient-fhirserver-99-all-client-id-xml/01-RegisterNewPatient",
            "connectathon-15-patient-fhirserver-99-all-client-id-xml/02-UpdatePatient",
            "connectathon-15-patient-fhirserver-99-all-client-id-xml/03-ReadPatient",
            "connectathon-15-patient-fhirserver-99-all-client-id-xml/05-VersionReadPatient",
            "connectathon-15-patient-fhirserver-99-all-client-id-xml/06-SearchPatient",
            "connectathon-15-patient-fhirserver-99-all-client-id-xml/07-PatientDelete",
            "connectathon-15-patient-fhirserver-99-all-server-id-json/01-RegisterNewPatient",
            "connectathon-15-patient-fhirserver-99-all-server-id-json/03-ReadPatient",
            "connectathon-15-patient-fhirserver-99-all-server-id-json/05-VersionReadPatient",
            "connectathon-15-patient-fhirserver-99-all-server-id-json/06-SearchPatient",
            "connectathon-15-patient-fhirserver-99-all-server-id-xml/01-RegisterNewPatient",
            "connectathon-15-patient-fhirserver-99-all-server-id-xml/03-ReadPatient",
            "connectathon-15-patient-fhirserver-99-all-server-id-xml/05-VersionReadPatient",
            "connectathon-15-patient-fhirserver-99-all-server-id-xml/06-SearchPatient",
            "connectathon-15-pd-load-resources-create-json/Step1-LoadTestResourceCreate",
            "connectathon-15-pd-load-resources-create-xml/Step1-LoadTestResourceCreate",
            "connectathon-15-practitionerrole-locate-direct-address-json/01-PractitionerRoleSearchPractitionerName",
            "connectathon-15-practitionerrole-locate-direct-address-json/02-PractitionerRoleSearchPractitionerFamilyGiven",
            "connectathon-15-practitionerrole-locate-direct-address-xml/01-PractitionerRoleSearchPractitionerName",
            "connectathon-15-practitionerrole-locate-direct-address-xml/02-PractitionerRoleSearchPractitionerFamilyGiven",
            "connectathon-15-practitionerrole-locate-telecom-json/01-PractitionerRoleSearchPractitionerIdentifier",
            "connectathon-15-practitionerrole-locate-telecom-json/02-PractitionerRoleSearchPractitionerName",
            "connectathon-15-practitionerrole-locate-telecom-json/03-PractitionerRoleSearchPractitionerFamilyGiven",
            "connectathon-15-practitionerrole-locate-telecom-json/04-PractitionerRoleSearchSpecialty",
            "connectathon-15-practitionerrole-locate-telecom-xml/01-PractitionerRoleSearchPractitionerIdentifier",
            "connectathon-15-practitionerrole-locate-telecom-xml/02-PractitionerRoleSearchPractitionerName",
            "connectathon-15-practitionerrole-locate-telecom-xml/03-PractitionerRoleSearchPractitionerFamilyGiven",
            "connectathon-15-practitionerrole-locate-telecom-xml/04-PractitionerRoleSearchSpecialty",
            "connectathon-15-scheduling-typical-flow-xml/Step1-CreateSupportingResources",
            "connectathon-15-scheduling-typical-flow-xml/Step2-CreateTheSchedule",
            "connectathon-15-scheduling-typical-flow-xml/Step3-SearchForFirstFreeSlot",
            "connectathonattachmenttracktest/A13_pdf1",
            "connectathonattachmenttracktest/A13_pdf2",
            "connectathonattachmenttracktest/A13_structured1",
            "connectathonattachmenttracktest/A13_structured2",
            "connectathonattachmenttracktest/A13_unstructured1",
            "connectathonattachmenttracktest/A13_unstructured2",
            "connectathonauditeventandprovenancetracktest/CAEP1",
            "connectathonauditeventandprovenancetracktest/CAEP2",
            "connectathonauditeventandprovenancetracktest/CAEP2X",
            "connectathonauditeventandprovenancetracktest/CAEP3",
            "connectathonauditeventandprovenancetracktest/CAEP4",
            "connectathonauditeventandprovenancetracktest/CAEP4X",
            "connectathonauditeventandprovenancetracktest/CAEP5",
            "connectathonfinancialtracktest/C9F_1C",
            "connectathonfinancialtracktest/C9F_1D",
            "connectathongenomicstracktest/CGT03",
            "connectathongenomicstracktest/CGT05",
            "connectathongenomicstracktest/CGT07",
            "connectathonpatchtracktest/C12PATCH_2_(JSON)",
            "connectathonpatchtracktest/C12PATCH_2_(XML)",
            "connectathonpatchtracktest/C12PATCH_3_(JSON)",
            "connectathonpatchtracktest/C12PATCH_3_(XML)",
            "history001/HI01",
            "history001/HI02",
            "history001/HI06",
            "history001/HI08",
            "history001/HI10",
            "history001/HI11",
            "readtest/R005",
            "resourcetest_adverseevent/X010_AdverseEvent",
            "resourcetest_adverseevent/X020_AdverseEvent",
            "resourcetest_claim/X010_Claim",
            "resourcetest_claim/X020_Claim",
            "resourcetest_claimresponse/X010_ClaimResponse",
            "resourcetest_claimresponse/X020_ClaimResponse",
            "resourcetest_imagingmanifest/X010_ImagingManifest",
            "resourcetest_imagingmanifest/X020_ImagingManifest",
            "search001/SE03G",
            "search001/SE03P",
            "search001/SE04G",
            "search001/SE04P",
            "search001/SE05.2G",
            "search001/SE05.2P",
            "search001/SE05.3G",
            "search001/SE05.3P",
            "search001/SE05.4G",
            "search001/SE05.4P",
            "search001/SE05.5G",
            "search001/SE05.5P",
            "search001/SE05.6G",
            "search001/SE05.6P",
            "search001/SE21G",
            "search001/SE21P",
            "search001/SE22G",
            "search001/SE22P",
            "search001/SE23G",
            "search001/SE23P",
            "search001/SE25G",
            "search001/SE25P",
            "searchtest_account/SE01G_Account",
            "searchtest_account/SE01P_Account",
            "searchtest_activitydefinition/SE01G_ActivityDefinition",
            "searchtest_activitydefinition/SE01P_ActivityDefinition",
            "searchtest_adverseevent/S000_AdverseEvent",
            "searchtest_adverseevent/SE01G_AdverseEvent",
            "searchtest_adverseevent/SE01P_AdverseEvent",
            "searchtest_allergyintolerance/SE01G_AllergyIntolerance",
            "searchtest_allergyintolerance/SE01P_AllergyIntolerance",
            "searchtest_appointment/SE01G_Appointment",
            "searchtest_appointment/SE01P_Appointment",
            "searchtest_appointmentresponse/SE01G_AppointmentResponse",
            "searchtest_appointmentresponse/SE01P_AppointmentResponse",
            "searchtest_basic/SE01G_Basic",
            "searchtest_basic/SE01P_Basic",
            "searchtest_binary/SE01G_Binary",
            "searchtest_binary/SE01P_Binary",
            "searchtest_bodysite/SE01G_BodySite",
            "searchtest_bodysite/SE01P_BodySite",
            "searchtest_bundle/SE01G_Bundle",
            "searchtest_bundle/SE01P_Bundle",
            "searchtest_capabilitystatement/SE01G_CapabilityStatement",
            "searchtest_capabilitystatement/SE01P_CapabilityStatement",
            "searchtest_careplan/SE01G_CarePlan",
            "searchtest_careplan/SE01P_CarePlan",
            "searchtest_careteam/SE01G_CareTeam",
            "searchtest_careteam/SE01P_CareTeam",
            "searchtest_chargeitem/SE01G_ChargeItem",
            "searchtest_chargeitem/SE01P_ChargeItem",
            "searchtest_claim/SE01G_Claim",
            "searchtest_claim/SE01P_Claim",
            "searchtest_claimresponse/SE01G_ClaimResponse",
            "searchtest_claimresponse/SE01P_ClaimResponse",
            "searchtest_clinicalimpression/SE01G_ClinicalImpression",
            "searchtest_clinicalimpression/SE01P_ClinicalImpression",
            "searchtest_codesystem/SE01G_CodeSystem",
            "searchtest_codesystem/SE01P_CodeSystem",
            "searchtest_communication/SE01G_Communication",
            "searchtest_communication/SE01P_Communication",
            "searchtest_communicationrequest/SE01G_CommunicationRequest",
            "searchtest_communicationrequest/SE01P_CommunicationRequest",
            "searchtest_compartmentdefinition/SE01G_CompartmentDefinition",
            "searchtest_compartmentdefinition/SE01P_CompartmentDefinition",
            "searchtest_composition/SE01G_Composition",
            "searchtest_composition/SE01P_Composition",
            "searchtest_conceptmap/SE01G_ConceptMap",
            "searchtest_conceptmap/SE01P_ConceptMap",
            "searchtest_condition/SE01G_Condition",
            "searchtest_condition/SE01P_Condition",
            "searchtest_consent/SE01G_Consent",
            "searchtest_consent/SE01P_Consent",
            "searchtest_contract/SE01G_Contract",
            "searchtest_contract/SE01P_Contract",
            "searchtest_coverage/SE01G_Coverage",
            "searchtest_coverage/SE01P_Coverage",
            "searchtest_dataelement/SE01G_DataElement",
            "searchtest_dataelement/SE01P_DataElement",
            "searchtest_detectedissue/SE01G_DetectedIssue",
            "searchtest_detectedissue/SE01P_DetectedIssue",
            "searchtest_device/SE01G_Device",
            "searchtest_device/SE01P_Device",
            "searchtest_devicecomponent/SE01G_DeviceComponent",
            "searchtest_devicecomponent/SE01P_DeviceComponent",
            "searchtest_devicemetric/SE01G_DeviceMetric",
            "searchtest_devicemetric/SE01P_DeviceMetric",
            "searchtest_devicerequest/SE01G_DeviceRequest",
            "searchtest_devicerequest/SE01P_DeviceRequest",
            "searchtest_deviceusestatement/SE01G_DeviceUseStatement",
            "searchtest_deviceusestatement/SE01P_DeviceUseStatement",
            "searchtest_diagnosticreport/SE01G_DiagnosticReport",
            "searchtest_diagnosticreport/SE01P_DiagnosticReport",
            "searchtest_documentmanifest/SE01G_DocumentManifest",
            "searchtest_documentmanifest/SE01P_DocumentManifest",
            "searchtest_documentreference/SE01G_DocumentReference",
            "searchtest_documentreference/SE01P_DocumentReference",
            "searchtest_eligibilityrequest/SE01G_EligibilityRequest",
            "searchtest_eligibilityrequest/SE01P_EligibilityRequest",
            "searchtest_eligibilityresponse/SE01G_EligibilityResponse",
            "searchtest_eligibilityresponse/SE01P_EligibilityResponse",
            "searchtest_encounter/SE01G_Encounter",
            "searchtest_encounter/SE01P_Encounter",
            "searchtest_endpoint/SE01G_Endpoint",
            "searchtest_endpoint/SE01P_Endpoint",
            "searchtest_enrollmentrequest/SE01G_EnrollmentRequest",
            "searchtest_enrollmentrequest/SE01P_EnrollmentRequest",
            "searchtest_enrollmentresponse/SE01G_EnrollmentResponse",
            "searchtest_enrollmentresponse/SE01P_EnrollmentResponse",
            "searchtest_episodeofcare/SE01G_EpisodeOfCare",
            "searchtest_episodeofcare/SE01P_EpisodeOfCare",
            "searchtest_expansionprofile/SE01G_ExpansionProfile",
            "searchtest_expansionprofile/SE01P_ExpansionProfile",
            "searchtest_explanationofbenefit/SE01G_ExplanationOfBenefit",
            "searchtest_explanationofbenefit/SE01P_ExplanationOfBenefit",
            "searchtest_familymemberhistory/SE01G_FamilyMemberHistory",
            "searchtest_familymemberhistory/SE01P_FamilyMemberHistory",
            "searchtest_flag/SE01G_Flag",
            "searchtest_flag/SE01P_Flag",
            "searchtest_goal/SE01G_Goal",
            "searchtest_goal/SE01P_Goal",
            "searchtest_graphdefinition/SE01G_GraphDefinition",
            "searchtest_graphdefinition/SE01P_GraphDefinition",
            "searchtest_group/SE01G_Group",
            "searchtest_group/SE01P_Group",
            "searchtest_guidanceresponse/SE01G_GuidanceResponse",
            "searchtest_guidanceresponse/SE01P_GuidanceResponse",
            "searchtest_healthcareservice/SE01G_HealthcareService",
            "searchtest_healthcareservice/SE01P_HealthcareService",
            "searchtest_imagingmanifest/SE01G_ImagingManifest",
            "searchtest_imagingmanifest/SE01P_ImagingManifest",
            "searchtest_imagingstudy/SE01G_ImagingStudy",
            "searchtest_imagingstudy/SE01P_ImagingStudy",
            "searchtest_immunization/SE01G_Immunization",
            "searchtest_immunization/SE01P_Immunization",
            "searchtest_immunizationrecommendation/SE01G_ImmunizationRecommendation",
            "searchtest_immunizationrecommendation/SE01P_ImmunizationRecommendation",
            "searchtest_implementationguide/SE01G_ImplementationGuide",
            "searchtest_implementationguide/SE01P_ImplementationGuide",
            "searchtest_library/SE01G_Library",
            "searchtest_library/SE01P_Library",
            "searchtest_linkage/SE01G_Linkage",
            "searchtest_linkage/SE01P_Linkage",
            "searchtest_list/SE01G_List",
            "searchtest_list/SE01P_List",
            "searchtest_location/SE01G_Location",
            "searchtest_location/SE01P_Location",
            "searchtest_measure/SE01G_Measure",
            "searchtest_measure/SE01P_Measure",
            "searchtest_measurereport/SE01G_MeasureReport",
            "searchtest_measurereport/SE01P_MeasureReport",
            "searchtest_media/SE01G_Media",
            "searchtest_media/SE01P_Media",
            "searchtest_medication/SE01G_Medication",
            "searchtest_medication/SE01P_Medication",
            "searchtest_medicationadministration/SE01G_MedicationAdministration",
            "searchtest_medicationadministration/SE01P_MedicationAdministration",
            "searchtest_medicationdispense/SE01G_MedicationDispense",
            "searchtest_medicationdispense/SE01P_MedicationDispense",
            "searchtest_medicationrequest/SE01G_MedicationRequest",
            "searchtest_medicationrequest/SE01P_MedicationRequest",
            "searchtest_medicationstatement/SE01G_MedicationStatement",
            "searchtest_medicationstatement/SE01P_MedicationStatement",
            "searchtest_messagedefinition/SE01G_MessageDefinition",
            "searchtest_messagedefinition/SE01P_MessageDefinition",
            "searchtest_messageheader/SE01G_MessageHeader",
            "searchtest_messageheader/SE01P_MessageHeader",
            "searchtest_namingsystem/SE01G_NamingSystem",
            "searchtest_namingsystem/SE01P_NamingSystem",
            "searchtest_nutritionorder/SE01G_NutritionOrder",
            "searchtest_nutritionorder/SE01P_NutritionOrder",
            "searchtest_observation/SE01G_Observation",
            "searchtest_observation/SE01P_Observation",
            "searchtest_operationdefinition/SE01G_OperationDefinition",
            "searchtest_operationdefinition/SE01P_OperationDefinition",
            "searchtest_organization/SE01G_Organization",
            "searchtest_organization/SE01P_Organization",
            "searchtest_patient/SE01G_Patient",
            "searchtest_patient/SE01P_Patient",
            "searchtest_paymentnotice/SE01G_PaymentNotice",
            "searchtest_paymentnotice/SE01P_PaymentNotice",
            "searchtest_paymentreconciliation/SE01G_PaymentReconciliation",
            "searchtest_paymentreconciliation/SE01P_PaymentReconciliation",
            "searchtest_person/SE01G_Person",
            "searchtest_person/SE01P_Person",
            "searchtest_plandefinition/SE01G_PlanDefinition",
            "searchtest_plandefinition/SE01P_PlanDefinition",
            "searchtest_practitioner/SE01G_Practitioner",
            "searchtest_practitioner/SE01P_Practitioner",
            "searchtest_practitionerrole/SE01G_PractitionerRole",
            "searchtest_practitionerrole/SE01P_PractitionerRole",
            "searchtest_procedure/SE01G_Procedure",
            "searchtest_procedure/SE01P_Procedure",
            "searchtest_procedurerequest/SE01G_ProcedureRequest",
            "searchtest_procedurerequest/SE01P_ProcedureRequest",
            "searchtest_processrequest/SE01G_ProcessRequest",
            "searchtest_processrequest/SE01P_ProcessRequest",
            "searchtest_processresponse/SE01G_ProcessResponse",
            "searchtest_processresponse/SE01P_ProcessResponse",
            "searchtest_provenance/SE01G_Provenance",
            "searchtest_provenance/SE01P_Provenance",
            "searchtest_questionnaire/SE01G_Questionnaire",
            "searchtest_questionnaire/SE01P_Questionnaire",
            "searchtest_questionnaireresponse/SE01G_QuestionnaireResponse",
            "searchtest_questionnaireresponse/SE01P_QuestionnaireResponse",
            "searchtest_referralrequest/SE01G_ReferralRequest",
            "searchtest_referralrequest/SE01P_ReferralRequest",
            "searchtest_relatedperson/SE01G_RelatedPerson",
            "searchtest_relatedperson/SE01P_RelatedPerson",
            "searchtest_requestgroup/SE01G_RequestGroup",
            "searchtest_requestgroup/SE01P_RequestGroup",
            "searchtest_researchstudy/SE01G_ResearchStudy",
            "searchtest_researchstudy/SE01P_ResearchStudy",
            "searchtest_researchsubject/SE01G_ResearchSubject",
            "searchtest_researchsubject/SE01P_ResearchSubject",
            "searchtest_riskassessment/SE01G_RiskAssessment",
            "searchtest_riskassessment/SE01P_RiskAssessment",
            "searchtest_schedule/SE01G_Schedule",
            "searchtest_schedule/SE01P_Schedule",
            "searchtest_searchparameter/SE01G_SearchParameter",
            "searchtest_searchparameter/SE01P_SearchParameter",
            "searchtest_sequence/SE01G_Sequence",
            "searchtest_sequence/SE01P_Sequence",
            "searchtest_servicedefinition/SE01G_ServiceDefinition",
            "searchtest_servicedefinition/SE01P_ServiceDefinition",
            "searchtest_slot/SE01G_Slot",
            "searchtest_slot/SE01P_Slot",
            "searchtest_specimen/SE01G_Specimen",
            "searchtest_specimen/SE01P_Specimen",
            "searchtest_structuredefinition/SE01G_StructureDefinition",
            "searchtest_structuredefinition/SE01P_StructureDefinition",
            "searchtest_structuremap/SE01G_StructureMap",
            "searchtest_structuremap/SE01P_StructureMap",
            "searchtest_subscription/SE01G_Subscription",
            "searchtest_subscription/SE01P_Subscription",
            "searchtest_substance/SE01G_Substance",
            "searchtest_substance/SE01P_Substance",
            "searchtest_supplydelivery/SE01G_SupplyDelivery",
            "searchtest_supplydelivery/SE01P_SupplyDelivery",
            "searchtest_supplyrequest/SE01G_SupplyRequest",
            "searchtest_supplyrequest/SE01P_SupplyRequest",
            "searchtest_task/SE01G_Task",
            "searchtest_task/SE01P_Task",
            "searchtest_testreport/SE01G_TestReport",
            "searchtest_testreport/SE01P_TestReport",
            "searchtest_testscript/SE01G_TestScript",
            "searchtest_testscript/SE01P_TestScript",
            "searchtest_valueset/SE01G_ValueSet",
            "searchtest_valueset/SE01P_ValueSet",
            "searchtest_visionprescription/SE01G_VisionPrescription",
            "searchtest_visionprescription/SE01P_VisionPrescription",
            "testscript-example/01-ReadPatient",
            "testscript-example/SETUP",
            "testscript-example-history/01-HistoryPatient",
            "testscript-example-history/SETUP",
            "testscript-example-readtest/R001",
            "testscript-example-readtest/R004",
            "testscript-example-search/02-PatientSearchDynamic",
            "testscript-example-search/SETUP",
            "testscript-example-update/SETUP",
            "transactionandbatchtest/XFER0",
            "transactionandbatchtest/XFER10",
            "transactionandbatchtest/XFER11",
            "transactionandbatchtest/XFER12",
        };

        private static readonly string[] KnownBroken =
        {
            "argonautproviderconnectathontest/APCT02",
            "connectathonschedulingtracktest/CST01",
            "resourcetest_devicerequest/X030_DeviceRequest", // Open issue: https://github.com/fhir-crucible/plan_executor/issues/136
            "resourcetest_questionnaire/X010_Questionnaire", // Open issue: https://github.com/fhir-crucible/plan_executor/issues/138
            "resourcetest_questionnaire/X020_Questionnaire", // Same issue as: https://github.com/fhir-crucible/plan_executor/issues/138
            "connectathon-15-patient-base-client-id-json/Step5-PatientDelete",
            "connectathon-15-patient-base-client-id-xml/Step5-PatientDelete",
            "connectathon-15-patient-base-server-id-json/Step5-PatientDelete",
            "connectathon-15-patient-base-server-id-xml/Step5-PatientDelete",
            "connectathon-15-patient-bonus-client-id-json/Step5-PatientDelete",
            "connectathon-15-patient-bonus-client-id-xml/Step5-PatientDelete",
            "connectathon-15-patient-bonus-server-id-json/Step5-PatientDelete",
            "connectathon-15-patient-bonus-server-id-xml/Step5-PatientDelete",
            "connectathon-15-patient-fhirserver-99-all-server-id-json/02-UpdatePatient",
            "connectathon-15-patient-fhirserver-99-all-server-id-xml/02-UpdatePatient",
            "connectathon-15-patient-fhirserver-99-all-server-id-json/07-PatientDelete",
            "connectathon-15-patient-fhirserver-99-all-server-id-xml/07-PatientDelete",
        };

        private readonly CrucibleDataSource _dataSource;
        private readonly ITestOutputHelper _output;

        public CrucibleTestFixture(CrucibleDataSource dataSource, ITestOutputHelper output)
        {
            _dataSource = dataSource;
            _output = output;
        }

        [Fact]
        public void CheckFailures()
        {
            var results = _dataSource?.TestRun?.Value?.TestRun?.TestResults;

            if (results != null)
            {
                var failures = results
                        .SelectMany(findTest => findTest.Result.Select(x =>
                        {
                            var testName = $"{x.TestId ?? findTest.TestId}/{x.Id}";
                            if (x.Status == "fail" && !KnownBroken.Contains(testName))
                            {
                                return $"{x.TestId ?? findTest.TestId}/{x.Id}";
                            }

                            return null;
                        }))
                    .Where(x => x != null)
                    .ToArray();

                Array.Sort(failures);

                _output.WriteLine("Current list of failures, see Run() for more details:");
                _output.WriteLine(string.Join(Environment.NewLine, failures));

                Assert.Equal(KnownFailures, failures);
            }
        }

        [Theory]
        [MemberData(nameof(GetTests))]
        [Trait(Traits.Category, Categories.Crucible)]
        public void Run(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return;
            }

            var findTest = _dataSource.TestRun.Value.TestRun.TestResults.FirstOrDefault(x => x.TestId == id);

            if (findTest != null)
            {
                var failures = findTest.Result
                    .Where(x =>
                    {
                        var testName = $"{x.TestId ?? findTest.TestId}/{x.Id}";
                        return x.Status == "fail" && !KnownFailures.Contains(testName) && !KnownBroken.Contains(testName);
                    })
                    .ToArray();

                if (failures.Any())
                {
                    var messages = failures
                        .Select(x =>
                            $"Failure in \"{x.TestId ?? findTest.TestId}/{x.Id}\", reason: \"{x.Message}\", description: \"{x.Description}\", see: {_dataSource.TestRun.Value.GetPermalink(x, findTest.TestId)}");

                    Assert.True(false, string.Join(Environment.NewLine, messages));
                }
                else
                {
                    var passing = findTest.Result.Where(x => x.Status == "pass").Select(x => $"{x.TestId ?? findTest.TestId}/{x.Id}").ToList();
                    if (passing.Count > 0)
                    {
                        _output.WriteLine($"Passing tests: {Environment.NewLine}{string.Join(Environment.NewLine, passing)}");
                    }

                    var failing = findTest.Result.Where(x => x.Status == "fail").Select(x => $"{x.TestId ?? findTest.TestId}/{x.Id}").ToList();
                    if (failing.Count > 0)
                    {
                        _output.WriteLine($"Excluded tests: {Environment.NewLine}{string.Join(Environment.NewLine, failing)}");
                    }
                }

                var shouldBeFailing = findTest.Result
                    .Where(x => x.Status == "pass" && KnownFailures.Contains($"{x.TestId ?? findTest.TestId}/{x.Id}"))
                    .ToArray();

                if (shouldBeFailing.Any())
                {
                    var messages = shouldBeFailing
                        .Select(x =>
                            $"Previously failing test \"{x.TestId ?? findTest.TestId}/{x.Id}\" is now passing, this should be removed from known failures.");

                    Assert.True(false, string.Join(Environment.NewLine, messages));
                }
            }
        }

        public static IEnumerable<object[]> GetTests()
        {
            var client = CrucibleDataSource.CreateClient();

            if (client == null)
            {
                return Enumerable.Repeat(new object[] { null }, 1);
            }

            var ids = CrucibleDataSource.GetSupportedIdsAsync(client).GetAwaiter().GetResult();

            return ids.Select(x => new[] { x }).ToArray();
        }
    }
}
