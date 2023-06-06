// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
using System.Net;
using System.Threading;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Test.Utilities;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Search
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.Json)]
    public class StringSearchTests : SearchTestsBase<StringSearchTestFixture>
    {
        private readonly HttpStatusCode[] _supportedHttpStatusCode = { HttpStatusCode.OK, HttpStatusCode.UnprocessableEntity };

        public StringSearchTests(StringSearchTestFixture fixture)
            : base(fixture)
        {
        }

        /// <summary>
        /// This can have two successful outcomes. We're not looking for results to come back but
        /// rather a 422 or 200 result. If we get one of those then that indicates the
        /// generated sql for a membermatch has been correctly handled by sql. Prior to the
        /// revision, we would get a 8623 error that sql can't create a sql query plan.
        /// This membermatch json will create up to 24 CTEs.
        /// </summary>
        [Fact(Skip= "Test does not succeed as expected. Bug assigned to Jared.")]
        [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer, Format.Json)]
        public async void GivenAComplexSqlStatement_FromMemberMatch_SucceedsWhenExecuted()
        {
            HttpStatusCode httpStatusCode = HttpStatusCode.OK;
            string body = Samples.GetJson("MemberMatch");
            try
            {
                await Client.PostAsync("Patient/$member-match", body, CancellationToken.None);
            }
            catch (Microsoft.Health.Fhir.Client.FhirClientException ex)
            {
                httpStatusCode = ex.StatusCode;
            }

            Assert.True(_supportedHttpStatusCode.Contains(httpStatusCode), $"HTTP Status Code '{httpStatusCode}' is not expected.");
        }

        [Fact]
        [HttpIntegrationFixtureArgumentSets(dataStores: DataStore.SqlServer)]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenChainedSearchQuery_WhenSearchedWithEqualsAndContains_ThenCorrectBundleShouldBeReturned()
        {
            var requestBundle = Samples.GetJsonSample("SearchDataBatch").ToPoco<Bundle>();
            using FhirResponse<Bundle> fhirResponse = await Client.PostBundleAsync(requestBundle);
            Assert.NotNull(fhirResponse);
            Assert.Equal(HttpStatusCode.OK, fhirResponse.StatusCode);

            string queryEquals = "_total=accurate&general-practitioner:Practitioner.name=Sarah&general-practitioner:Practitioner.address-state=Wa";
            string queryContains = "_total=accurate&general-practitioner:Practitioner.name=Sarah&general-practitioner:Practitioner.address-state:contains=Wa";

            Bundle bundleEquals = await Client.SearchAsync(ResourceType.Patient, queryEquals);
            Bundle bundleContains = await Client.SearchAsync(ResourceType.Patient, queryContains);

            Assert.NotNull(bundleEquals);
            Assert.NotNull(bundleContains);
            Assert.True(bundleEquals.Total <= bundleContains.Total);
            Assert.True(bundleEquals.Total > 0 && bundleContains.Total > 0);
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
            string query = $"address-city{modifier}={valueToSearch}&_tag={Fixture.FixtureTag}";

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
            string query = $"address-city{modifier}={valueToSearch}&_tag={Fixture.FixtureTag}";

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
            Bundle bundle = await Client.SearchAsync(ResourceType.Patient, $"family=Smith,Ander&_tag={Fixture.FixtureTag}");

            ValidateBundle(bundle, Fixture.Patients[0], Fixture.Patients[2]);
        }

        [Fact]
        public async Task GivenAStringSearchParamThatCoversSeveralFields_WhenSpecifiedTwiceInASearch_IntersectsTheTwoResultsProperly()
        {
            Bundle bundle = await Client.SearchAsync(ResourceType.Patient, $"name=Bea&name=Smith&_tag={Fixture.FixtureTag}");

            ValidateBundle(bundle, Fixture.Patients[0]);
        }

        [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer, Format.Json)]
        [Theory]
        [Trait(Traits.Priority, Priority.One)]
        [InlineData("muller")]
        [InlineData("müller")]
        public async Task GivenAStringSearchParamWithAccentAndAResourceWithAccent_WhenSearched_ThenCorrectBundleShouldBeReturned(string searchText)
        {
            string query = $"name={searchText}&_total=accurate&_tag={Fixture.FixtureTag}";

            Bundle bundle = await Client.SearchAsync(ResourceType.Patient, query);

            Assert.NotNull(bundle);
            Assert.Equal(2, bundle.Total);
            Assert.NotEmpty(bundle.Entry);
        }

        [Fact]
        public async Task GivenAEscapedStringSearchParams_WhenSearched_ThenCorrectBundleShouldBeReturned()
        {
            Bundle bundle = await Client.SearchAsync(ResourceType.Patient, $"name=Richard\\,Muller&_tag={Fixture.FixtureTag}");

            ValidateBundle(bundle, Fixture.Patients[7]);
        }
    }
}
