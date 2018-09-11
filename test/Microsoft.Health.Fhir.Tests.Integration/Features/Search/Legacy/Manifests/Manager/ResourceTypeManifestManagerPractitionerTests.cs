// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Hl7.Fhir.Model;
using Xunit;
using static Microsoft.Health.Fhir.Tests.Integration.Features.Search.TestHelper;

namespace Microsoft.Health.Fhir.Tests.Integration.Features.Search.Legacy.Manifests.Manager
{
    public class ResourceTypeManifestManagerPractitionerTests : ResourceTypeManifestManagerTests<Practitioner>
    {
        private readonly Practitioner _practitioner = new Practitioner();

        protected override Practitioner Resource => _practitioner;

        [Fact]
        public void GivenAPractitionerWithActive_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestActive(p => p.Active);
        }

        [Fact]
        public void GivenAPractitionerWithAddressCity_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestAddressCity(p => p.Address);
        }

        [Fact]
        public void GivenAPractitionerWithAddressCountry_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestAddressCountry(p => p.Address);
        }

        [Fact]
        public void GivenAPractitionerWithAddressPostalCode_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestAddressPostalCode(p => p.Address);
        }

        [Fact]
        public void GivenAPractitionerWithAddressState_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestAddressState(p => p.Address);
        }

        [Fact]
        public void GivenAPractitionerWithAddressUse_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestAddressUse(p => p.Address);
        }

        [Fact]
        public void GivenAPractitionerWithLanguage_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestTokenSearchParam(
                "communication",
                () =>
                {
                    _practitioner.Communication = new List<CodeableConcept>
                    {
                        CodeableConcept3WithText,
                        CodeableConcept1WithText,
                        CodeableConcept2,
                    };
                },
                CodingsForCodeableConcept3WithText,
                CodingsForCodeableConcept1WithText,
                CodingsForCodeableConcept2);
        }

        [Fact]
        public void GivenAPractitionerWithEmail_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestEmail(p => p.Telecom);
        }

        [Fact]
        public void GivenAPractitionerWithFamily_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestFamily(p => p.Name);
        }

        [Fact]
        public void GivenAPractitionerWithGender_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestGender(p => p.Gender);
        }

        [Fact]
        public void GivenAPractitionerWithGiven_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestGiven(p => p.Name);
        }

        [Fact]
        public void GivenAPractitionerWithIdentifier_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestIdentifier(p => p.Identifier);
        }

        [Fact]
        public void GivenAPractitionerWithPhone_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestPhone(p => p.Telecom);
        }

        [Fact]
        public void GivenAPractitionerWithTelecom_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestTelecom(p => p.Telecom);
        }

        [Fact]
        public void GivenAPractitionerWithName_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestName(p => p.Name);
        }

        [Fact]
        public void GivenAPractitionerWithAddress_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestAddress(p => p.Address);
        }
    }
}
