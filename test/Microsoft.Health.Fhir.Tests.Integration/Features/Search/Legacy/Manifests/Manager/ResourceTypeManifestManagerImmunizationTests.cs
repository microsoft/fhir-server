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
    public class ResourceTypeManifestManagerImmunizationTests : ResourceTypeManifestManagerTests<Immunization>
    {
        private readonly Immunization _immunization = new Immunization();

        protected override Immunization Resource => _immunization;

        [Fact]
        public void GivenAnImmunizationWithDate_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestSearchParam(
                "date",
                () =>
                {
                    _immunization.Date = DateTime1;
                },
                ValidateDateTime,
                DateTime1);
        }

        [Fact]
        public void GivenAnImmunizationWithDoseSequence_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestSearchParam(
                "dose-sequence",
                () =>
                {
                    _immunization.VaccinationProtocol = new List<Immunization.VaccinationProtocolComponent>
                    {
                        new Immunization.VaccinationProtocolComponent
                        {
                            DoseSequence = (int)Number1,
                        },
                    };
                },
                ValidateNumber,
                Number1);
        }

        [Fact]
        public void GivenAnImmunizationWithIdentifier_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestIdentifier(i => i.Identifier);
        }

        [Fact]
        public void GivenAnImmunizationWithLocation_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestSearchParam(
                "location",
                () => { _immunization.Location = new ResourceReference(LocationReference); },
                ValidateReference,
                LocationReference);
        }

        [Fact]
        public void GivenAnImmunizationWithLotNumber_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestSearchParam(
                "lot-number",
                () => { _immunization.LotNumber = String1; },
                ValidateString,
                String1);
        }

        [Fact]
        public void GivenAnImmunizationWithManufacturer_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestSearchParam(
                "manufacturer",
                () => { _immunization.Manufacturer = new ResourceReference(OrganizationReference); },
                ValidateReference,
                OrganizationReference);
        }

        [Fact]
        public void GivenAnImmunizationWithNotGiven_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestTokenSearchParam(
                "notgiven",
                () =>
                {
                    _immunization.NotGivenElement = new FhirBoolean(true);
                },
                CodingTrue);
        }

        [Fact]
        public void GivenAnImmunizationWithPatient_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestSearchParam(
                "patient",
                () => { _immunization.Patient = new ResourceReference(PatientReference); },
                ValidateReference,
                PatientReference);
        }

        [Fact]
        public void GivenAnImmunizationWithPractitioner_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestSearchParam(
                "practitioner",
                () =>
                {
                    _immunization.Practitioner = new List<Immunization.PractitionerComponent>
                    {
                        new Immunization.PractitionerComponent
                        {
                            Actor = new ResourceReference(PractitionerReference),
                        },
                    };
                },
                ValidateReference,
                PractitionerReference);
        }

        [Fact]
        public void GivenAnImmunizationWithReaction_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestSearchParam(
                "reaction",
                () =>
                {
                    _immunization.Reaction = new List<Immunization.ReactionComponent>
                    {
                        new Immunization.ReactionComponent
                        {
                            Detail = new ResourceReference(ObservationReference),
                        },
                    };
                },
                ValidateReference,
                ObservationReference);
        }

        [Fact]
        public void GivenAnImmunizationWithReactionDate_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestSearchParam(
                "reaction-date",
                () =>
                {
                    _immunization.Reaction = new List<Immunization.ReactionComponent>
                    {
                        new Immunization.ReactionComponent
                        {
                            Date = DateTime1,
                        },
                    };
                },
                ValidateDateTime,
                DateTime1);
        }

        [Fact]
        public void GivenAnImmunizationWithReason_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestTokenSearchParam(
                "reason",
                () =>
                {
                    _immunization.Explanation = new Immunization.ExplanationComponent
                    {
                        Reason = new List<CodeableConcept> { CodeableConcept1WithText },
                    };
                },
                CodingsForCodeableConcept1WithText);
        }

        [Fact]
        public void GivenAnImmunizationWithReasonNotGiven_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestTokenSearchParam(
                "reason-not-given",
                () =>
                {
                    _immunization.Explanation = new Immunization.ExplanationComponent
                    {
                        ReasonNotGiven = new List<CodeableConcept> { CodeableConcept1WithText },
                    };
                },
                CodingsForCodeableConcept1WithText);
        }

        [Fact]
        public void GivenAnImmunizationWithStatus_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestTokenSearchParam(
                "status",
                () =>
                {
                    _immunization.Status = Immunization.ImmunizationStatusCodes.Completed;
                },
                new Coding("http://hl7.org/fhir/medication-admin-status", "completed"));
        }

        [Fact]
        public void GivenAnImmunizationWithVaccineCode_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestTokenSearchParam(
                "vaccine-code",
                () =>
                {
                    _immunization.VaccineCode = CodeableConcept1WithText;
                },
                CodingsForCodeableConcept1WithText);
        }
    }
}
