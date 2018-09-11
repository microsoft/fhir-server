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
    public class ResourceTypeManifestManagerOrganizationTests : ResourceTypeManifestManagerTests<Organization>
    {
        private readonly Organization _organization = new Organization();

        protected override Organization Resource => _organization;

        [Fact]
        public void GivenAnOrganizationWithActive_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestActive(o => o.Active);
        }

        [Fact]
        public void GivenAnOrganizationWithAddressCity_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestAddressCity(o => o.Address);
        }

        [Fact]
        public void GivenAnOrganizationWithAddressCountry_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestAddressCountry(o => o.Address);
        }

        [Fact]
        public void GivenAnOrganizationWithAddressPostalCode_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestAddressPostalCode(o => o.Address);
        }

        [Fact]
        public void GivenAnOrganizationWithAddressState_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestAddressState(o => o.Address);
        }

        [Fact]
        public void GivenAnOrganizationWithAddressUse_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestAddressUse(o => o.Address);
        }

        [Fact]
        public void GivenAnOrganizationWithEndpoint_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestSearchParam(
                "endpoint",
                () =>
                {
                    _organization.Endpoint = new System.Collections.Generic.List<ResourceReference>
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
        public void GivenAnOrganizationWithIdentifier_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestIdentifier(o => o.Identifier);
        }

        [Fact]
        public void GivenAnOrganizationWithName_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestSearchParam(
                "name",
                () =>
                {
                    _organization.Name = String1;
                },
                ValidateString,
                String1);
        }

        [Fact]
        public void GivenAnOrganizationWithPartOf_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestSearchParam(
                "partof",
                () =>
                {
                    _organization.PartOf = new ResourceReference(PatientReference);
                },
                ValidateReference,
                PatientReference);
        }

        [Fact]
        public void GivenAnOrganizationWithType_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestTokenSearchParam(
                "type",
                () =>
                {
                    _organization.Type = new System.Collections.Generic.List<CodeableConcept>()
                    {
                        CodeableConcept2,
                        CodeableConcept1WithText,
                    };
                },
                CodingsForCodeableConcept2,
                CodingsForCodeableConcept1WithText);
        }
    }
}
