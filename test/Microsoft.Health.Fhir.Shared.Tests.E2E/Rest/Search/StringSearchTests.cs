// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Search
{
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.Json)]
    public class StringSearchTests : SearchTestsBase<StringSearchTestFixture>
    {
        public StringSearchTests(StringSearchTestFixture fixture)
            : base(fixture)
        {
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
            string query = string.Format("address-city{0}={1}", modifier, valueToSearch);

            Bundle bundle = await Client.SearchAsync(ResourceType.Patient, query);

            Assert.NotNull(bundle);

            Patient expectedPatient = Fixture.Patients[0];

            if (shouldMatch)
            {
                Assert.NotEmpty(bundle.Entry);
                Assert.Collection(
                    bundle.Entry,
                    e => Assert.True(expectedPatient.IsExactly(e.Resource)));
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
        [InlineData("", StringSearchTestFixture.LongString, true)]
        [InlineData("", "Not" + StringSearchTestFixture.LongString, false)]
        [InlineData(":exact", StringSearchTestFixture.LongString, true)]
        [InlineData(":exact",  StringSearchTestFixture.LongString + "Not", false)]
        [InlineData(":contains", StringSearchTestFixture.LongString, true)]
        [InlineData(":contains", StringSearchTestFixture.LongString + "Not", false)]
        [InlineData(":contains", "Vestibulum", true)]
        [InlineData(":contains", "NotInString", false)]
        public async Task GivenAStringSearchParamAndAResourceWithALongSearchParamValue_WhenSearched_ThenCorrectBundleShouldBeReturned(string modifier, string valueToSearch, bool shouldMatch)
        {
            string query = string.Format("address-city{0}={1}", modifier, valueToSearch);

            Bundle bundle = await Client.SearchAsync(ResourceType.Patient, query);

            Assert.NotNull(bundle);

            Patient expectedPatient = Fixture.Patients[3];

            if (shouldMatch)
            {
                Assert.NotEmpty(bundle.Entry);
                Assert.Collection(
                    bundle.Entry,
                    e => Assert.True(expectedPatient.IsExactly(e.Resource)));
            }
            else
            {
                Assert.Empty(bundle.Entry);
            }
        }

        [Fact]
        public async Task GivenAStringSearchParamWithMultipleValues_WhenSearched_ThenCorrectBundleShouldBeReturned()
        {
            Bundle bundle = await Client.SearchAsync(ResourceType.Patient, "family=Smith,Ander");

            ValidateBundle(bundle, Fixture.Patients[0], Fixture.Patients[2]);
        }
    }
}
