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
    [Trait(Traits.OwningTeam, OwningTeam.FhirImport)]
    [Trait(Traits.Category, Categories.Import)]
    [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer, Format.Json)]
    public class ImportStringSearchTests : IClassFixture<ImportStringSearchTestFixture>
    {
        private readonly TestFhirClient _client;
        private readonly ImportStringSearchTestFixture _fixture;

        public ImportStringSearchTests(ImportStringSearchTestFixture fixture)
        {
            _client = fixture.TestFhirClient;
            _fixture = fixture;
        }

        [Theory]
        [Trait(Traits.Priority, Priority.One)]
        [InlineData("", "seattle", true)]
        [InlineData("", "SEATTLE", true)]
        [InlineData("", "Seattle", true)]
        [InlineData("", "Sea", true)]
        [InlineData("", "sea", true)]
        [InlineData("", "123", false)]
        [InlineData(":exact", "Seattle", true)]
        [InlineData(":exact", "seattle", false)]
        [InlineData(":exact", "SEATTLE", false)]
        [InlineData(":exact", "Sea", false)]
        [InlineData(":contains", "att", true)]
        [InlineData(":contains", "EAT", true)]
        [InlineData(":contains", "123", false)]
        public async Task GivenAStringSearchParam_WhenSearched_ThenCorrectBundleShouldBeReturned(string modifier, string valueToSearch, bool shouldMatch)
        {
            string query = string.Format("address-city{0}={1}&_tag={2}", modifier, valueToSearch, _fixture.FixtureTag);

            Bundle bundle = await _client.SearchAsync(ResourceType.Patient, query);

            Assert.NotNull(bundle);

            Patient expectedPatient = _fixture.Patients[0];

            if (shouldMatch)
            {
                Assert.NotEmpty(bundle.Entry);
                ImportTestHelper.VerifyBundle(bundle, expectedPatient);
            }
            else
            {
                Assert.Empty(bundle.Entry);
            }
        }

        [Theory]
        [Trait(Traits.Priority, Priority.One)]
        [InlineData("", "Lorem", true)]
        [InlineData("", "NotLorem", false)]
        [InlineData("", ImportStringSearchTestFixture.LongString, true)]
        [InlineData("", "Not" + ImportStringSearchTestFixture.LongString, false)]
        [InlineData(":exact", ImportStringSearchTestFixture.LongString, true)]
        [InlineData(":exact", ImportStringSearchTestFixture.LongString + "Not", false)]
        [InlineData(":contains", ImportStringSearchTestFixture.LongString, true)]
        [InlineData(":contains", ImportStringSearchTestFixture.LongString + "Not", false)]
        [InlineData(":contains", "Vestibulum", true)]
        [InlineData(":contains", "NotInString", false)]
        public async Task GivenAStringSearchParamAndAResourceWithALongSearchParamValue_WhenSearched_ThenCorrectBundleShouldBeReturned(string modifier, string valueToSearch, bool shouldMatch)
        {
            string query = string.Format("address-city{0}={1}&_tag={2}", modifier, valueToSearch, _fixture.FixtureTag);

            Bundle bundle = await _client.SearchAsync(ResourceType.Patient, query);

            Assert.NotNull(bundle);

            Patient expectedPatient = _fixture.Patients[3];

            if (shouldMatch)
            {
                Assert.NotEmpty(bundle.Entry);
                ImportTestHelper.VerifyBundle(bundle, expectedPatient);
            }
            else
            {
                Assert.Empty(bundle.Entry);
            }
        }

        [Fact]
        public async Task GivenAStringSearchParamWithMultipleValues_WhenSearched_ThenCorrectBundleShouldBeReturned()
        {
            Bundle bundle = await _client.SearchAsync(ResourceType.Patient, $"family=Smith,Ander&_tag={_fixture.FixtureTag}");

            ImportTestHelper.VerifyBundle(bundle, _fixture.Patients[0], _fixture.Patients[2]);
        }

        [Fact]
        public async Task GivenAStringSearchParamThatCoversSeveralFields_WhenSpecifiedTwiceInASearch_IntersectsTheTwoResultsProperly()
        {
            Bundle bundle = await _client.SearchAsync(ResourceType.Patient, $"name=Bea&name=Smith&_tag={_fixture.FixtureTag}");

            ImportTestHelper.VerifyBundle(bundle, _fixture.Patients[0]);
        }

        [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer, Format.Json)]
        [Theory]
        [Trait(Traits.Priority, Priority.One)]
        [InlineData("muller")]
        [InlineData("müller")]
        public async Task GivenAStringSearchParamWithAccentAndAResourceWithAccent_WhenSearched_ThenCorrectBundleShouldBeReturned(string searchText)
        {
            string query = $"name={searchText}&_total=accurate&_tag={_fixture.FixtureTag}";

            Bundle bundle = await _client.SearchAsync(ResourceType.Patient, query);

            Assert.NotNull(bundle);
            Assert.Equal(2, bundle.Total);
            Assert.NotEmpty(bundle.Entry);
        }

        [Fact]
        public async Task GivenAEscapedStringSearchParams_WhenSearched_ThenCorrectBundleShouldBeReturned()
        {
            Bundle bundle = await _client.SearchAsync(ResourceType.Patient, $"name=Richard\\,Muller&_tag={_fixture.FixtureTag}");

            ImportTestHelper.VerifyBundle(bundle, _fixture.Patients[7]);
        }
    }
}
