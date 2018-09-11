// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.Model;
using Xunit;
using static Microsoft.Health.Fhir.Tests.Common.Search.SearchValueValidationHelper;
using static Microsoft.Health.Fhir.Tests.Integration.Features.Search.TestHelper;

namespace Microsoft.Health.Fhir.Tests.Integration.Features.Search.Legacy.Manifests
{
    public class ResourceTypeManifestManagerRelatedPersonTests : ResourceTypeManifestManagerTests<RelatedPerson>
    {
        private readonly RelatedPerson _relatedPerson = new RelatedPerson();

        protected override RelatedPerson Resource => _relatedPerson;

        [Fact]
        public void GivenARelatedPersonWithActive_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestActive(p => p.Active);
        }

        [Fact]
        public void GivenARelatedPersonWithAddressCity_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestAddressCity(p => p.Address);
        }

        [Fact]
        public void GivenARelatedPersonWithAddressCountry_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestAddressCountry(p => p.Address);
        }

        [Fact]
        public void GivenARelatedPersonWithAddressPostalCode_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestAddressPostalCode(p => p.Address);
        }

        [Fact]
        public void GivenARelatedPersonWithAddressState_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestAddressState(p => p.Address);
        }

        [Fact]
        public void GivenARelatedPersonWithAddressUse_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestAddressUse(p => p.Address);
        }

        [Fact]
        public void GivenARelatedPersonWithBirthdate_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestBirthdate(p => p.BirthDate);
        }

        [Fact]
        public void GivenARelatedPersonWithEmail_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestEmail(p => p.Telecom);
        }

        [Fact]
        public void GivenARelatedPersonWithGender_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestGender(p => p.Gender);
        }

        [Fact]
        public void GivenARelatedPersonWithIdentifier_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestIdentifier(p => p.Identifier);
        }

        [Fact]
        public void GivenARelatedPersonWithPatient_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestSearchParam(
                "patient",
                () =>
                {
                    _relatedPerson.Patient = new ResourceReference(PatientReference);
                },
                ValidateReference,
                PatientReference);
        }

        [Fact]
        public void GivenARelatedPersonWithPhone_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestPhone(p => p.Telecom);
        }

        [Fact]
        public void GivenARelatedPersonWithTelecom_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestTelecom(p => p.Telecom);
        }

        [Fact]
        public void GivenARelatedPersonWithName_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestName(p => p.Name);
        }

        [Fact]
        public void GivenARelatedPersonWithAddress_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestAddress(p => p.Address);
        }
    }
}
