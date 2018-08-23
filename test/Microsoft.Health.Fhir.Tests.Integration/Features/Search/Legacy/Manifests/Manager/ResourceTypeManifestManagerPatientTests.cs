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
    public class ResourceTypeManifestManagerPatientTests : ResourceTypeManifestManagerTests<Patient>
    {
        private readonly Patient _patient = new Patient();

        protected override Patient Resource => _patient;

        [Fact]
        public void GivenAPatientWithActive_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestActive(p => p.Active);
        }

        [Fact]
        public void GivenAPatientWithAddressCity_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestAddressCity(p => p.Address);
        }

        [Fact]
        public void GivenAPatientWithAddressCountry_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestAddressCountry(p => p.Address);
        }

        [Fact]
        public void GivenAPatientWithAddressPostalCode_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestAddressPostalCode(p => p.Address);
        }

        [Fact]
        public void GivenAPatientWithAddressState_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestAddressState(p => p.Address);
        }

        [Fact]
        public void GivenAPatientWithAddressUse_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestAddressUse(p => p.Address);
        }

        [Fact]
        public void GivenAPatientWithAnimalBreed_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestTokenSearchParam(
                "animal-breed",
                () =>
                {
                    _patient.Animal = new Patient.AnimalComponent()
                    {
                        Breed = CodeableConcept1WithText,
                    };
                },
                CodingsForCodeableConcept1WithText);
        }

        [Fact]
        public void GivenAPatientWithAnimalSpecies_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestTokenSearchParam(
                "animal-species",
                () =>
                {
                    _patient.Animal = new Patient.AnimalComponent()
                    {
                        Species = CodeableConcept2,
                    };
                },
                CodingsForCodeableConcept2);
        }

        [Fact]
        public void GivenAPatientWithBirthdate_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestBirthdate(p => p.BirthDate);
        }

        [Fact]
        public void GivenAPatientWithDeathDate_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestSearchParam(
                "death-date",
                () =>
                {
                    _patient.Deceased = new FhirDateTime(DateTime1);
                },
                ValidateDateTime,
                DateTime1);
        }

        [Fact]
        public void GivenAPatientWithDeceasedAsBoolean_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            // Boolean
            TestTokenSearchParam(
                "deceased",
                () =>
                {
                    _patient.Deceased = new FhirBoolean(true);
                },
                CodingTrue);
        }

        [Fact]
        public void GivenAPatientWithDeceasedAsDateTime_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            // DateTime
            TestTokenSearchParam(
                "deceased",
                () =>
                {
                    _patient.Deceased = new FhirDateTime(DateTime1);
                },
                CodingTrue);
        }

        [Fact]
        public void GivenAPatientWithEmail_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestEmail(p => p.Telecom);
        }

        [Fact]
        public void GivenAPatientWithFamily_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestFamily(p => p.Name);
        }

        [Fact]
        public void GivenAPatientWithGender_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestGender(p => p.Gender);
        }

        [Fact]
        public void GivenAPatientWithGeneralPractitioner_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestSearchParam(
                "general-practitioner",
                () =>
                {
                    _patient.GeneralPractitioner = new List<ResourceReference>()
                    {
                        new ResourceReference(PatientReference),
                        new ResourceReference(OrganizationReference),
                    };
                },
                ValidateReference,
                PatientReference,
                OrganizationReference);
        }

        [Fact]
        public void GivenAPatientWithGiven_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestGiven(p => p.Name);
        }

        [Fact]
        public void GivenAPatientWithIdentifier_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestIdentifier(p => p.Identifier);
        }

        [Fact]
        public void GivenAPatientWithLanguage_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestTokenSearchParam(
                "language",
                () =>
                {
                    _patient.Communication = new List<Patient.CommunicationComponent>()
                    {
                        new Patient.CommunicationComponent() { Language = CodeableConcept1WithText },
                        new Patient.CommunicationComponent() { Language = CodeableConcept2 },
                    };
                },
                CodingsForCodeableConcept1WithText,
                CodingsForCodeableConcept2);
        }

        [Fact]
        public void GivenAPatientWithLink_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestSearchParam(
                "link",
                () =>
                {
                    _patient.Link = new List<Patient.LinkComponent>()
                    {
                        new Patient.LinkComponent() { Other = new ResourceReference(PatientReference) },
                        new Patient.LinkComponent() { Other = new ResourceReference(OrganizationReference) },
                    };
                },
                ValidateReference,
                PatientReference,
                OrganizationReference);
        }

        [Fact]
        public void GivenAPatientWithOrganization_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestSearchParam(
                "organization",
                () =>
                {
                    _patient.ManagingOrganization = new ResourceReference(PatientReference);
                },
                ValidateReference,
                PatientReference);
        }

        [Fact]
        public void GivenAPatientWithPhone_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestPhone(p => p.Telecom);
        }

        [Fact]
        public void GivenAPatientWithTelecom_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestTelecom(p => p.Telecom);
        }

        [Fact]
        public void GivenAPatientWithName_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestName(p => p.Name);
        }

        [Fact]
        public void GivenAPatientWithAddress_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestAddress(p => p.Address);
        }
    }
}
