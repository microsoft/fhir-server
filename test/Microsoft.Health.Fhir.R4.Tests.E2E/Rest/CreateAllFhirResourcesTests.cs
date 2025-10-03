// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Hl7.Fhir.ElementModel;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    /// <summary>
    /// Provides R4 resource creation tests.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.Json)]
    public partial class CreateAllFhirResourcesTests : IClassFixture<CreateAllFhirResourcesTestsFixture>
    {
        private readonly CreateAllFhirResourcesTestsFixture _fixture;
        private readonly TestFhirClient _client;

        public CreateAllFhirResourcesTests(CreateAllFhirResourcesTestsFixture fixture)
        {
            _fixture = fixture;
            _client = fixture.TestFhirClient;
        }

        [Theory]
        [MemberData(nameof(GetResourceFileNames))]
        public async Task GivenValidResources_WhenCreateIsCalled_ShouldBeCreatedSuccessfully(object fileName)
        {
            var resourceJson = Samples.GetJsonSample((string)fileName).ToPoco();
            using var resource = await _client.CreateAsync(resourceJson);
            Assert.Equal(System.Net.HttpStatusCode.Created, resource.StatusCode);

            _fixture.ResourcesToCleanup.Add($"{resource.Resource.TypeName}/{resource.Resource.Id}");
        }

        public static IEnumerable<object[]> GetResourceFileNames()
        {
            yield return new object[] { "account-example" };
            yield return new object[] { "activitydefinition-example" };
            yield return new object[] { "adverseevent-example" };
            yield return new object[] { "allergyintolerance-example" };
            yield return new object[] { "appointment-example" };
            yield return new object[] { "appointmentresponse-example" };
            yield return new object[] { "auditevent-example" };
            yield return new object[] { "basic-example" };
            yield return new object[] { "binary-example" };
            yield return new object[] { "biologicallyderivedproduct-example" };
            yield return new object[] { "bodystructure-example-fetus" };
            yield return new object[] { "Bundle-Batch" };
            yield return new object[] { "capabilitystatement-example" };
            yield return new object[] { "CarePlan" };
            yield return new object[] { "careteam-example" };
            yield return new object[] { "catalogentry-example" };
            yield return new object[] { "chargeitemdefinition-device-example" };
            yield return new object[] { "chargeitem-example" };
            yield return new object[] { "claim-example" };
            yield return new object[] { "claimresponse-example" };
            yield return new object[] { "clinicalimpression-example" };
            yield return new object[] { "codesystem-abstract-types" };
            yield return new object[] { "communication-example" };
            yield return new object[] { "communicationrequest-example" };
            yield return new object[] { "compartmentdefinition-device" };
            yield return new object[] { "composition-example" };
            yield return new object[] { "conceptmap-example" };
            yield return new object[] { "condition-example2" };
            yield return new object[] { "consent-example" };
            yield return new object[] { "contract-example" };
            yield return new object[] { "coverageeligibilityrequest-example" };
            yield return new object[] { "coverageeligibilityresponse-example" };
            yield return new object[] { "coverage-example" };
            yield return new object[] { "detectedissue-example" };
            yield return new object[] { "Device-d1" };
            yield return new object[] { "devicemetric-example" };
            yield return new object[] { "devicedefinition-example" };
            yield return new object[] { "devicerequest-example" };
            yield return new object[] { "deviceusestatement-example" };
            yield return new object[] { "diagnosticreport-example" };
            yield return new object[] { "documentmanifest-example" };
            yield return new object[] { "documentreference-example" };
            yield return new object[] { "effectevidencesynthesis-example" };
            yield return new object[] { "encounter-example" };
            yield return new object[] { "endpoint-example" };
            yield return new object[] { "enrollmentrequest-example" };
            yield return new object[] { "enrollmentresponse-example" };
            yield return new object[] { "episodeofcare-example" };
            yield return new object[] { "eventdefinition-example" };
            yield return new object[] { "evidence-example" };
            yield return new object[] { "evidencevariable-example" };
            yield return new object[] { "examplescenario-example" };
            yield return new object[] { "explanationofbenefit-example" };
            yield return new object[] { "familymemberhistory-example-mother" };
            yield return new object[] { "flag-example" };
            yield return new object[] { "goal-example" };
            yield return new object[] { "graphdefinition-example" };
            yield return new object[] { "group-example" };
            yield return new object[] { "guidanceresponse-example" };
            yield return new object[] { "healthcareservice-example" };
            yield return new object[] { "imagingstudy-example" };
            yield return new object[] { "immunizationevaluation-example" };
            yield return new object[] { "immunizationrecommendation.profile" };
            yield return new object[] { "implementationguide-example" };
            yield return new object[] { "insuranceplan-example" };
            yield return new object[] { "invoice-example" };
            yield return new object[] { "library-example" };
            yield return new object[] { "linkage-example" };
            yield return new object[] { "list-example" };
            yield return new object[] { "location-example" };
            yield return new object[] { "measure-cms146-example" };
            yield return new object[] { "media-example" };
            yield return new object[] { "medicationadministration0301" };
            yield return new object[] { "medicationdispense0301" };
            yield return new object[] { "medicationexample1" };
            yield return new object[] { "medicationknowledge-example" };
            yield return new object[] { "medicationrequest0302" };
            yield return new object[] { "medicationstatementexample1" };
            yield return new object[] { "MedicinalProduct" };
            yield return new object[] { "medicinalproductauthorization-example" };
            yield return new object[] { "medicinalproductcontraindication-questionnaire" };
            yield return new object[] { "medicinalproductindication-example" };
            yield return new object[] { "medicinalproductingredient-example" };
            yield return new object[] { "medicinalproductinteraction-example" };
            yield return new object[] { "medicinalproductmanufactured-example" };
            yield return new object[] { "medicinalproductpackaged-example" };
            yield return new object[] { "medicinalproductpharmaceutical-example" };
            yield return new object[] { "medicinalproductundesirableeffect-example" };
            yield return new object[] { "messagedefinition-example" };
            yield return new object[] { "messageheader-example" };
            yield return new object[] { "molecularsequence-example" };
            yield return new object[] { "namingsystem-example" };
            yield return new object[] { "nutritionorder-example-cardiacdiet" };
            yield return new object[] { "observation-example" };
            yield return new object[] { "ObservationDefinition-example" };
            yield return new object[] { "operationdefinition-example" };
            yield return new object[] { "operationoutcome-example" };
            yield return new object[] { "Organization" };
            yield return new object[] { "organizationaffiliation-example" };
            yield return new object[] { "paymentnotice-example" };
            yield return new object[] { "paymentreconciliation-example" };
            yield return new object[] { "person-example" };
            yield return new object[] { "Patient" };
            yield return new object[] { "plandefinition-example" };
            yield return new object[] { "practitioner-example" };
            yield return new object[] { "practitionerrole-questionnaire" };
            yield return new object[] { "procedure-example" };
            yield return new object[] { "provenance-example" };
            yield return new object[] { "questionnaire-example" };
            yield return new object[] { "questionnaireresponse-example" };
            yield return new object[] { "relatedperson-example" };
            yield return new object[] { "requestgroup-example" };
            yield return new object[] { "researchdefinition-example" };
            yield return new object[] { "researchelementdefinition-example" };
            yield return new object[] { "researchstudy-example" };
            yield return new object[] { "researchsubject-example" };
            yield return new object[] { "riskassessment-example" };
            yield return new object[] { "riskevidencesynthesis-example" };
            yield return new object[] { "schedule-example" };
            yield return new object[] { "servicerequest-example" };
            yield return new object[] { "slot-example" };
            yield return new object[] { "specimendefinition-questionnaire" };
            yield return new object[] { "specimen-example" };
            yield return new object[] { "structuredefinition-example-composition" };
            yield return new object[] { "structuremap-example" };
            yield return new object[] { "subscription-example" };
            yield return new object[] { "substance-example" };
            yield return new object[] { "substancenucleicacid-example" };
            yield return new object[] { "substancepolymer-example" };
            yield return new object[] { "substanceprotein-example" };
            yield return new object[] { "substancereferenceinformation-example" };
            yield return new object[] { "substancesourcematerial-example" };
            yield return new object[] { "substancespecification-example" };
            yield return new object[] { "supplydelivery-example" };
            yield return new object[] { "supplyrequest-example-simpleorder" };
            yield return new object[] { "task-example1" };
            yield return new object[] { "terminologycapabilities-example" };
            yield return new object[] { "testreport-example" };
            yield return new object[] { "testscript-example" };
            yield return new object[] { "ValueSet" };
            yield return new object[] { "verificationresult-example" };
            yield return new object[] { "visionprescription-example" };
        }
    }
}
