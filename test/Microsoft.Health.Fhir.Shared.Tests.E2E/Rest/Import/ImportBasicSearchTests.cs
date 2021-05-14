// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Import
{
    [Trait(Traits.Category, Categories.Import)]
    [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer, Format.Json)]
    public class ImportBasicSearchTests : IClassFixture<ImportBasicSearchTestFixture>
    {
        private readonly TestFhirClient _client;
        private readonly ImportBasicSearchTestFixture _fixture;

        public ImportBasicSearchTests(ImportBasicSearchTestFixture fixture)
        {
            _client = fixture.TestFhirClient;
            _fixture = fixture;

            _fixture.InitailizeAsync().Wait();
        }

        [Fact]
        public async Task GivenImportedResourceWithVariousValues_WhenSearchedWithMultipleParams_ThenOnlyResourcesMatchingAllSearchParamsShouldBeReturned()
        {
            Patient patientAddressCityAndFamily = _fixture.PatientAddressCityAndFamily;
            string query = string.Format("Patient?address-city={0}&family={1}", patientAddressCityAndFamily.Address[0].City, patientAddressCityAndFamily.Name[0].Family);

            await ImportTestHelper.VerifySearchResultAsync(_fixture.TestFhirClient, query, patientAddressCityAndFamily);
        }

        [Fact]
        public async Task GivenImportedResourceWithVariousValues_WhenSearchedWithCityParam_ThenOnlyResourcesMatchingAllSearchParamsShouldBeReturned()
        {
            string query = string.Format("Patient?address-city={0}", _fixture.PatientWithSameCity1.Address[0].City);

            await ImportTestHelper.VerifySearchResultAsync(_fixture.TestFhirClient, query, _fixture.PatientWithSameCity1, _fixture.PatientWithSameCity2);
        }

        [Fact]
        public async Task GivenImportedResourceWithVariousValues_WhenSearchedWithTheMissingModifer_ThenOnlyTheResourcesWithMissingOrPresentParametersAreReturned()
        {
            string queryMissingFalse = string.Format("Patient?gender:missing=false", _fixture.PatientWithSameCity1.Address[0].City);
            await ImportTestHelper.VerifySearchResultAsync(_fixture.TestFhirClient, queryMissingFalse, _fixture.PatientWithGender);

            string queryMissing = string.Format("Patient?gender:missing=true", _fixture.PatientWithSameCity1.Address[0].City);
            await ImportTestHelper.VerifySearchResultAsync(_fixture.TestFhirClient, queryMissing, _fixture.PatientAddressCityAndFamily, _fixture.PatientWithSameCity1, _fixture.PatientWithSameCity2);
        }
    }
}
