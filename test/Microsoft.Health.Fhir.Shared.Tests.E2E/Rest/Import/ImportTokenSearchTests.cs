// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Import
{
    [Trait("Traits.OwningTeam", OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Import)]
    [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer, Format.Json)]
    public class ImportTokenSearchTests : IClassFixture<ImportTokenSearchTestFixture>
    {
        public static readonly object[][] TokenSearchParameterData = new[]
        {
            new object[] { "a" },
            new object[] { "code1", 0, 5, 6 },
            new object[] { "code3", 4, 6, 7 },
            new object[] { "a|b" },
            new object[] { "system2|code2", 1 },
            new object[] { "|code2" },
            new object[] { "|code3", 7 },
            new object[] { "a|" },
            new object[] { "system3|", 4, 5, 6 },
            new object[] { "code1,system2|code2", 0, 1, 5, 6 },
        };

        private readonly TestFhirClient _client;
        private readonly ImportTokenSearchTestFixture _fixture;

        public ImportTokenSearchTests(ImportTokenSearchTestFixture fixture)
        {
            _client = fixture.TestFhirClient;
            _fixture = fixture;
        }

        [Theory]
        [MemberData(nameof(TokenSearchParameterData))]
        public async Task GivenATokenSearchParameter_WhenSearched_ThenCorrectBundleShouldBeReturned(string queryValue, params int[] expectedIndices)
        {
            Bundle bundle = await _client.SearchAsync(ResourceType.Observation, $"value-concept={queryValue}&_tag={_fixture.FixtureTag}");

            Observation[] expected = expectedIndices.Select(i => _fixture.Observations[i]).ToArray();

            ImportTestHelper.VerifyBundle(bundle, expected);
        }

        [Theory]
        [InlineData("code1")]
        [InlineData("text", 2, 3, 4, 5, 6)]
        [InlineData("text2", 3, 6)]
        public async Task GivenATokenSearchParameterWithTextModifier_WhenSearched_ThenCorrectBundleShouldBeReturned(string queryValue, params int[] expectedIndices)
        {
            Bundle bundle = await _client.SearchAsync(ResourceType.Observation, $"value-concept:text={queryValue}&_tag={_fixture.FixtureTag}");

            Observation[] expected = expectedIndices.Select(i => _fixture.Observations[i]).ToArray();

            ImportTestHelper.VerifyBundle(bundle, expected);
        }

        [Theory]
        [MemberData(nameof(TokenSearchParameterData))]
        public async Task GivenATokenSearchParameterWithNotModifier_WhenSearched_ThenCorrectBundleShouldBeReturned(string queryValue, params int[] excludeIndices)
        {
            Bundle bundle = await _client.SearchAsync(ResourceType.Observation, $"value-concept:not={queryValue}&_tag={_fixture.FixtureTag}");

            Observation[] expected = _fixture.Observations.Where((_, i) => !excludeIndices.Contains(i)).ToArray();

            ImportTestHelper.VerifyBundle(bundle, expected);
        }

        [Fact]
        public async Task GivenATokenSearchParameterWithNotModifier_WhenSearchedOverMissingValue_ThenCorrectBundleShouldBeReturned()
        {
            Bundle bundle = await _client.SearchAsync(ResourceType.Observation, $"category:not=test&_tag={_fixture.FixtureTag}");

            Observation[] expected = _fixture.Observations.Where((_, i) => i != 8).ToArray();

            ImportTestHelper.VerifyBundle(bundle, expected);
        }

        [Theory]
        [InlineData("code1", 0, 5, 6, 8)]
        public async Task GivenMultipleTokenSearchParametersWithNotModifiers_WhenSearched_ThenCorrectBundleShouldBeReturned(string queryValue, params int[] excludeIndices)
        {
            Bundle bundle = await _client.SearchAsync(ResourceType.Observation, $"category:not=test&value-concept:not={queryValue}&_tag={_fixture.FixtureTag}");

            Observation[] expected = _fixture.Observations.Where((_, i) => !excludeIndices.Contains(i)).ToArray();

            ImportTestHelper.VerifyBundle(bundle, expected);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        public async Task GivenIdWithNotModifier_WhenSearched_ThenCorrectBundleShouldBeReturned(int count)
        {
            Bundle bundle = await _client.SearchAsync(ResourceType.Observation, $"_id:not={string.Join(",", _fixture.Observations.Take(count).Select(x => x.Id))}&_tag={_fixture.FixtureTag}");

            Observation[] expected = _fixture.Observations.Skip(count).ToArray();

            ImportTestHelper.VerifyBundle(bundle, expected);
        }

        [Theory]
        [InlineData(ResourceType.Patient)]
        [InlineData(ResourceType.Patient, ResourceType.Organization)]
        public async Task GivenTypeWithNotModifier_WhenSearched_ThenCorrectBundleShouldBeReturned(params ResourceType[] resourceTypes)
        {
            Bundle bundle = await _client.SearchAsync($"?_tag={_fixture.FixtureTag}&_type:not={string.Join(",", resourceTypes)}");

            ImportTestHelper.VerifyBundle(bundle, _fixture.Observations.ToArray());
        }

        [Theory]
        [MemberData(nameof(TokenSearchParameterData))]
        public async Task GivenATokenSearchParameterWithNotModifier_WhenSearchedWithType_ThenCorrectBundleShouldBeReturned(string queryValue, params int[] excludeIndices)
        {
            Bundle bundle = await _client.SearchAsync($"?_type={ResourceType.Observation}&value-concept:not={queryValue}&_tag={_fixture.FixtureTag}");

            Observation[] expected = _fixture.Observations.Where((_, i) => !excludeIndices.Contains(i)).ToArray();

            ImportTestHelper.VerifyBundle(bundle, expected);
        }
    }
}
