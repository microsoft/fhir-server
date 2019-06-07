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
        public TokenSearchTests(TokenSearchTestFixture fixture)
            : base(fixture)
        {
        }

        [Theory]
        [InlineData("a")]
        [InlineData("code1", 0, 5, 6)]
        [InlineData("code3", 4, 6, 7)]
        [InlineData("a|b")]
        [InlineData("system2|code2", 1)]
        [InlineData("|code2")]
        [InlineData("|code3", 7)]
        [InlineData("a|")]
        [InlineData("system3|", 4, 5, 6)]
        public async Task GivenATokenSearchParameter_WhenSearched_ThenCorrectBundleShouldBeReturned(string queryValue, params int[] expectedIndices)
        {
            Bundle bundle = await Client.SearchAsync(ResourceType.Observation, $"value-concept={queryValue}");

            Observation[] expected = expectedIndices.Select(i => Fixture.Observations[i]).ToArray();

            ValidateBundle(bundle, expected);
        }

        [Theory]
        [InlineData("code1")]
        [InlineData("text", 2, 3, 4, 5, 6)]
        [InlineData("text2", 3, 6)]
        public async Task GivenATokenSearchParameterWithTextModifier_WhenSearched_ThenCorrectBundleShouldBeReturned(string queryValue, params int[] expectedIndices)
        {
            Bundle bundle = await Client.SearchAsync(ResourceType.Observation, $"value-concept:text={queryValue}");

            Observation[] expected = expectedIndices.Select(i => Fixture.Observations[i]).ToArray();

            ValidateBundle(bundle, expected);
        }
    }
}
