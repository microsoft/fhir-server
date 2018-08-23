// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Hl7.Fhir.Model;
using Xunit;
using static Microsoft.Health.Fhir.Tests.Common.Search.SearchValueValidationHelper;
using static Microsoft.Health.Fhir.Tests.Integration.Features.Search.TestHelper;

namespace Microsoft.Health.Fhir.Tests.Integration.Features.Search.Legacy.Manifests
{
    public class ResourceTypeManifestManagerConditionTests : ResourceTypeManifestManagerTests<Condition>
    {
        private readonly Condition _condition = new Condition();

        protected override Condition Resource => _condition;

        [Fact]
        public void GivenAConditionWithAbatementAge_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestSearchParam(
                "abatement-age",
                () =>
                {
                    _condition.Abatement = Age1;
                },
                ValidateQuantity,
                Age1);
        }

        [Fact]
        public void GivenAConditionWithAbatementBooleanFromBoolean_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestTokenSearchParam(
                "abatement-boolean",
                () =>
                {
                    _condition.Abatement = FhirBooleanTrue;
                },
                CodingTrue);
        }

        [Fact]
        public void GivenAConditionWithAbatementBooleanFromAge_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestTokenSearchParam(
                "abatement-boolean",
                () =>
                {
                    _condition.Abatement = Age1;
                },
                CodingTrue);
        }

        [Fact]
        public void GivenAConditionWithAbatementBooleanFromString_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestTokenSearchParam(
                "abatement-boolean",
                () =>
                {
                    _condition.Abatement = FhirString1;
                },
                CodingTrue);
        }

        [Fact]
        public void GivenAConditionWithAbatementBooleanFromPeriod_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestTokenSearchParam(
                "abatement-boolean",
                () =>
                {
                    _condition.Abatement = Period1;
                },
                CodingTrue);
        }

        [Fact]
        public void GivenAConditionWithAbatementBooleanFromRange_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestTokenSearchParam(
                "abatement-boolean",
                () =>
                {
                    _condition.Abatement = Range1;
                },
                CodingTrue);
        }

        [Fact]
        public void GivenAConditionWithAbatementBooleanFromDateTime_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestTokenSearchParam(
                "abatement-boolean",
                () =>
                {
                    _condition.Abatement = FhirDateTime1;
                },
                CodingTrue);
        }

        [Fact]
        public void GivenAConditionWithAbatementDateFromDateTime_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestSearchParam(
                "abatement-date",
                () =>
                {
                    _condition.Abatement = FhirDateTime1;
                },
                ValidateDateTime,
                DateTime1);
        }

        [Fact]
        public void GivenAConditionWithAbatementDateFromPeriod_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestSearchParam(
                "abatement-date",
                () =>
                {
                    _condition.Abatement = Period1;
                },
                ValidateDateTime,
                "2018");
        }

        [Fact]
        public void GivenAConditionWithAbatementString_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestSearchParam(
                "abatement-string",
                () =>
                {
                    _condition.Abatement = FhirString1;
                },
                ValidateString,
                String1);
        }

        [Fact]
        public void GivenAConditionWithAssertedDate_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestSearchParam(
                "asserted-date",
                () =>
                {
                    _condition.AssertedDate = DateTime1;
                },
                ValidateDateTime,
                DateTime1);
        }

        [Fact]
        public void GivenAConditionWithAsserter_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestSearchParam(
                "asserter",
                () => { _condition.Asserter = new ResourceReference(PractitionerReference); },
                ValidateReference,
                PractitionerReference);
        }

        [Fact]
        public void GivenAConditionWithBodySite_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestTokenSearchParam(
                "body-site",
                () => { _condition.BodySite = new List<CodeableConcept> { CodeableConcept3WithText }; },
                CodingsForCodeableConcept3WithText);
        }

        [Fact]
        public void GivenAConditionWithCategory_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestTokenSearchParam(
                "category",
                () => { _condition.Category = new List<CodeableConcept> { CodeableConcept1WithText }; },
                CodingsForCodeableConcept1WithText);
        }

        [Fact]
        public void GivenAConditionWithClinicalStatus_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestTokenSearchParam(
                "clinical-status",
                () => { _condition.ClinicalStatus = Condition.ConditionClinicalStatusCodes.Active; },
                new Coding("http://hl7.org/fhir/condition-clinical", "active"));
        }

        [Fact]
        public void GivenAConditionWithCode_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestTokenSearchParam(
                "code",
                () => { _condition.Code = CodeableConcept2; },
                CodingsForCodeableConcept2);
        }

        [Fact]
        public void GivenAConditionWithContext_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestSearchParam(
                "context",
                () => { _condition.Context = new ResourceReference(EncounterReference); },
                ValidateReference,
                EncounterReference);
        }

        [Fact]
        public void GivenAConditionWithEncounter_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestSearchParam(
                "encounter",
                () => { _condition.Context = new ResourceReference(EncounterReference); },
                ValidateReference,
                EncounterReference);
        }

        [Fact]
        public void GivenAConditionWithEvidence_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestTokenSearchParam(
                "evidence",
                () =>
                {
                    _condition.Evidence = new List<Condition.EvidenceComponent>
                    {
                        new Condition.EvidenceComponent
                        {
                            Code = new List<CodeableConcept> { CodeableConcept1WithText },
                        },
                    };
                },
                CodingsForCodeableConcept1WithText);
        }

        [Fact]
        public void GivenAConditionWithEvidenceDetail_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestSearchParam(
                "evidence-detail",
                () =>
                {
                    _condition.Evidence = new List<Condition.EvidenceComponent>
                    {
                        new Condition.EvidenceComponent
                        {
                            Detail = new List<ResourceReference> { new ResourceReference(ObservationReference) },
                        },
                    };
                },
                ValidateReference,
                ObservationReference);
        }

        [Fact]
        public void GivenAConditionWithIdentifier_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestIdentifier(c => c.Identifier);
        }

        [Fact]
        public void GivenAConditionWithOnsetAge_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestSearchParam(
                "onset-age",
                () =>
                {
                    _condition.Onset = Age1;
                },
                ValidateQuantity,
                Age1);
        }

        [Fact]
        public void GivenAConditionWithOnsetDateFromDateTime_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestSearchParam(
                "onset-date",
                () =>
                {
                    _condition.Onset = FhirDateTime1;
                },
                ValidateDateTime,
                DateTime1);
        }

        [Fact]
        public void GivenAConditionWithOnsetDateFromPeriod_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestSearchParam(
                "onset-date",
                () =>
                {
                    _condition.Onset = Period1;
                },
                ValidateDateTime,
                "2018");
        }

        [Fact]
        public void GivenAConditionWithOnsetString_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestSearchParam(
                "onset-info",
                () =>
                {
                    _condition.Onset = FhirString1;
                },
                ValidateString,
                String1);
        }

        [Fact]
        public void GivenAConditionWithPatient_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestSearchParam(
                "patient",
                () => { _condition.Subject = new ResourceReference(PatientReference); },
                ValidateReference,
                PatientReference);
        }

        [Fact]
        public void GivenAConditionWithSubject_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestSearchParam(
                "subject",
                () => { _condition.Subject = new ResourceReference(PatientReference); },
                ValidateReference,
                PatientReference);
        }

        [Fact]
        public void GivenAConditionWithSeverity_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestTokenSearchParam(
                "severity",
                () => { _condition.Severity = CodeableConcept1WithText; },
                CodingsForCodeableConcept1WithText);
        }

        [Fact]
        public void GivenAConditionWithStage_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestTokenSearchParam(
                "stage",
                () => { _condition.Stage = new Condition.StageComponent { Summary = CodeableConcept1WithText }; },
                CodingsForCodeableConcept1WithText);
        }

        [Fact]
        public void GivenAConditionWithVerificationStatus_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestTokenSearchParam(
                "verification-status",
                () =>
                {
                    _condition.VerificationStatus = Condition.ConditionVerificationStatus.Confirmed;
                },
                new Coding("http://hl7.org/fhir/condition-ver-status", "confirmed"));
        }
    }
}
