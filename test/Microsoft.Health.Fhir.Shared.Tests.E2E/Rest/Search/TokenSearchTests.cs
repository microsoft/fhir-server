// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Search
{
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.Json)]
    public class TokenSearchTests : SearchTestsBase<TokenSearchTestFixture>
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

        public static readonly object[][] IdentiferSearchParameterData = new[]
        {
            new object[] { "http://terminology.hl7.org/CodeSystem/v2-0203|RRI|1234", ":ofType", 1 },
            new object[] { "http://terminology.hl7.org/CodeSystem/v2-0203|PN|ab", ":ofType", 2 },
            new object[] { "http://terminology.hl7.org/CodeSystem/v3-GenderStatus|I|ab", ":ofType", 2 },
            new object[] { "http://terminology.hl7.org/CodeSystem/v2-0203|ab", string.Empty, 3 },
            new object[] { "http://terminology.hl7.org/CodeSystem/v2-0203|", string.Empty, 3, 4 },
            new object[] { "|ab", string.Empty, 2, 5 },
            new object[] { "ab", string.Empty, 2, 3, 5 },
        };

        public TokenSearchTests(TokenSearchTestFixture fixture)
            : base(fixture)
        {
        }

        [Theory]
        [MemberData(nameof(TokenSearchParameterData))]
        public async Task GivenATokenSearchParameter_WhenSearched_ThenCorrectBundleShouldBeReturned(string queryValue, params int[] expectedIndices)
        {
            Bundle bundle = await Client.SearchAsync(ResourceType.Observation, $"_tag={Fixture.Tag}&value-concept={queryValue}");

            Observation[] expected = expectedIndices.Select(i => Fixture.Observations[i]).ToArray();

            ValidateBundle(bundle, expected);
        }

        [Theory]
        [InlineData("code1")]
        [InlineData("text", 2, 3, 4, 5, 6)]
        [InlineData("text2", 3, 6)]
        public async Task GivenATokenSearchParameterWithTextModifier_WhenSearched_ThenCorrectBundleShouldBeReturned(string queryValue, params int[] expectedIndices)
        {
            Bundle bundle = await Client.SearchAsync(ResourceType.Observation, $"_tag={Fixture.Tag}&value-concept:text={queryValue}");

            Observation[] expected = expectedIndices.Select(i => Fixture.Observations[i]).ToArray();

            ValidateBundle(bundle, expected);
        }

        [Theory]
        [MemberData(nameof(TokenSearchParameterData))]
        public async Task GivenATokenSearchParameterWithNotModifier_WhenSearched_ThenCorrectBundleShouldBeReturned(string queryValue, params int[] excludeIndices)
        {
            Bundle bundle = await Client.SearchAsync(ResourceType.Observation, $"_tag={Fixture.Tag}&value-concept:not={queryValue}");

            Observation[] expected = Fixture.Observations.Where((_, i) => !excludeIndices.Contains(i)).ToArray();

            ValidateBundle(bundle, expected);
        }

        [Fact]
        public async Task GivenATokenSearchParameterWithNotModifier_WhenSearchedOverMissingValue_ThenCorrectBundleShouldBeReturned()
        {
            Bundle bundle = await Client.SearchAsync(ResourceType.Observation, $"_tag={Fixture.Tag}&category:not=test");

            Observation[] expected = Fixture.Observations.Where((_, i) => i != 8).ToArray();

            ValidateBundle(bundle, expected);
        }

        [Theory]
        [InlineData("code1", 0, 5, 6, 8)]
        public async Task GivenMultipleTokenSearchParametersWithNotModifiers_WhenSearched_ThenCorrectBundleShouldBeReturned(string queryValue, params int[] excludeIndices)
        {
            Bundle bundle = await Client.SearchAsync(ResourceType.Observation, $"_tag={Fixture.Tag}&category:not=test&value-concept:not={queryValue}");

            Observation[] expected = Fixture.Observations.Where((_, i) => !excludeIndices.Contains(i)).ToArray();

            ValidateBundle(bundle, expected);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        public async Task GivenIdWithNotModifier_WhenSearched_ThenCorrectBundleShouldBeReturned(int count)
        {
            Bundle bundle = await Client.SearchAsync(ResourceType.Observation, $"_tag={Fixture.Tag}&_id:not={string.Join(",", Fixture.Observations.Take(count).Select(x => x.Id))}");

            Observation[] expected = Fixture.Observations.Skip(count).ToArray();

            ValidateBundle(bundle, expected);
        }

        [Theory]
        [InlineData(ResourceType.Patient)]
        [InlineData(ResourceType.Patient, ResourceType.Organization)]
        public async Task GivenTypeWithNotModifier_WhenSearched_ThenCorrectBundleShouldBeReturned(params ResourceType[] resourceTypes)
        {
            Bundle bundle = await Client.SearchAsync($"?_tag={Fixture.Tag}&_type:not={string.Join(",", resourceTypes)}");

            ValidateBundle(bundle, Fixture.Observations.ToArray());
        }

        [Theory]
        [MemberData(nameof(TokenSearchParameterData))]
        public async Task GivenATokenSearchParameterWithNotModifier_WhenSearchedWithType_ThenCorrectBundleShouldBeReturned(string queryValue, params int[] excludeIndices)
        {
            Bundle bundle = await Client.SearchAsync($"?_tag={Fixture.Tag}&_type={ResourceType.Observation}&value-concept:not={queryValue}");

            Observation[] expected = Fixture.Observations.Where((_, i) => !excludeIndices.Contains(i)).ToArray();

            ValidateBundle(bundle, expected);
        }

        [Theory]
        [MemberData(nameof(IdentiferSearchParameterData))]
        public async Task GivenAIdentiferSearch_WhenSearched_ThenCorrectBundleShouldBeReturned(string queryValue, string modifier, params int[] includeIndices)
        {
            Bundle bundle = await Client.SearchAsync(ResourceType.Patient, $"_tag={Fixture.Tag}&identifier{modifier}={queryValue}");

            Patient[] expected = Fixture.Patients.Where((_, i) => includeIndices.Contains(i)).ToArray();

            ValidateBundle(bundle, expected);
        }
    }
}
