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
    public class QuantitySearchTests : SearchTestsBase<QuantitySearchTestFixture>
    {
        public QuantitySearchTests(QuantitySearchTestFixture fixture)
            : base(fixture)
        {
        }

        [Theory]
        [InlineData("30")]
        [InlineData("5", 3, 4)]
        [InlineData("5.000", 3, 4)]
        [InlineData("eq30")]
        [InlineData("eq5", 3, 4)]
        [InlineData("eq5.000", 3, 4)]
        [InlineData("ne5", 0, 1, 2, 5, 6, 7)]
        [InlineData("ne5.000", 0, 1, 2, 5, 6, 7)]
        [InlineData("lt4.9", 0, 1, 2)]
        [InlineData("lt5", 0, 1, 2)]
        [InlineData("lt5.000", 0, 1, 2)]
        [InlineData("lt5.01", 0, 1, 2, 3, 4)]
        [InlineData("gt4.9", 3, 4, 5, 6, 7)]
        [InlineData("gt5", 5, 6, 7)]
        [InlineData("gt5.000", 5, 6, 7)]
        [InlineData("gt5.1", 5, 6, 7)]
        [InlineData("le4.9", 0, 1, 2)]
        [InlineData("le5", 0, 1, 2, 3, 4)]
        [InlineData("le5.000", 0, 1, 2, 3, 4)]
        [InlineData("le5.0001", 0, 1, 2, 3, 4)]
        [InlineData("ge4.9999", 3, 4, 5, 6, 7)]
        [InlineData("ge5", 3, 4, 5, 6, 7)]
        [InlineData("ge5.000", 3, 4, 5, 6, 7)]
        [InlineData("ge5.001", 5, 6, 7)]
        [InlineData("sa4.9999", 3, 4, 5, 6, 7)]
        [InlineData("sa5", 5, 6, 7)]
        [InlineData("eb5", 0, 1, 2)]
        [InlineData("eb5.0001", 0, 1, 2, 3, 4)]
        [InlineData("30|system1")]
        [InlineData("5|system1", 3)]
        [InlineData("5.000|system1", 3)]
        [InlineData("eq30|system1")]
        [InlineData("eq5|system1", 3)]
        [InlineData("eq5.000|system1", 3)]
        [InlineData("ne5|system1", 0, 2, 6, 7)]
        [InlineData("ne5.000|system1", 0, 2, 6, 7)]
        [InlineData("lt4.9|system1", 0, 2)]
        [InlineData("lt5|system1", 0, 2)]
        [InlineData("lt5.000|system1", 0, 2)]
        [InlineData("lt5.01|system1", 0, 2, 3)]
        [InlineData("gt4.9|system1", 3, 6, 7)]
        [InlineData("gt5|system1", 6, 7)]
        [InlineData("gt5.000|system1", 6, 7)]
        [InlineData("gt5.1|system1", 6, 7)]
        [InlineData("le4.9|system1", 0, 2)]
        [InlineData("le5|system1", 0, 2, 3)]
        [InlineData("le5.000|system1", 0, 2, 3)]
        [InlineData("le5.0001|system1", 0, 2, 3)]
        [InlineData("ge4.9999|system1", 3, 6, 7)]
        [InlineData("ge5|system1", 3, 6, 7)]
        [InlineData("ge5.000|system1", 3, 6, 7)]
        [InlineData("ge5.001|system1", 6, 7)]
        [InlineData("sa4.9999|system1", 3, 6, 7)]
        [InlineData("sa5|system1", 6, 7)]
        [InlineData("eb5|system1", 0, 2)]
        [InlineData("eb5.0001|system1", 0, 2, 3)]
        [InlineData("30||unit2")]
        [InlineData("5||unit2", 4)]
        [InlineData("5.000||unit2", 4)]
        [InlineData("eq30||unit2")]
        [InlineData("eq5||unit2", 4)]
        [InlineData("eq5.000||unit2", 4)]
        [InlineData("ne5||unit2", 5, 6)]
        [InlineData("ne5.000||unit2", 5, 6)]
        [InlineData("lt4.9||unit2")]
        [InlineData("lt5||unit2")]
        [InlineData("lt5.000||unit2")]
        [InlineData("lt5.01||unit2", 4)]
        [InlineData("gt4.9||unit2", 4, 5, 6)]
        [InlineData("gt5||unit2", 5, 6)]
        [InlineData("gt5.000||unit2", 5, 6)]
        [InlineData("gt5.1||unit2", 5, 6)]
        [InlineData("le4.9||unit2")]
        [InlineData("le5||unit2", 4)]
        [InlineData("le5.000||unit2", 4)]
        [InlineData("le5.0001||unit2", 4)]
        [InlineData("ge4.9999||unit2", 4, 5, 6)]
        [InlineData("ge5||unit2", 4, 5, 6)]
        [InlineData("ge5.000||unit2", 4, 5, 6)]
        [InlineData("ge5.001||unit2", 5, 6)]
        [InlineData("sa4.9999||unit2", 4, 5, 6)]
        [InlineData("sa5||unit2", 5, 6)]
        [InlineData("eb5||unit2")]
        [InlineData("eb5.0001||unit2", 4)]
        [InlineData("30|system1|unit2")]
        [InlineData("5|system1|unit2")]
        [InlineData("5.000|system1|unit2")]
        [InlineData("eq30|system1|unit2")]
        [InlineData("eq5|system1|unit2")]
        [InlineData("eq5.000|system1|unit2")]
        [InlineData("ne5|system1|unit2", 6)]
        [InlineData("ne5.000|system1|unit2", 6)]
        [InlineData("lt4.9|system1|unit2")]
        [InlineData("lt5|system1|unit2")]
        [InlineData("lt5.000|system1|unit2")]
        [InlineData("lt5.01|system1|unit2")]
        [InlineData("gt4.9|system1|unit2", 6)]
        [InlineData("gt5|system1|unit2", 6)]
        [InlineData("gt5.000|system1|unit2", 6)]
        [InlineData("gt5.1|system1|unit2", 6)]
        [InlineData("le4.9|system1|unit2")]
        [InlineData("le5|system1|unit2")]
        [InlineData("le5.000|system1|unit2")]
        [InlineData("le5.0001|system1|unit2")]
        [InlineData("ge4.9999|system1|unit2", 6)]
        [InlineData("ge5|system1|unit2", 6)]
        [InlineData("ge5.000|system1|unit2", 6)]
        [InlineData("ge5.001|system1|unit2", 6)]
        [InlineData("sa4.9999|system1|unit2", 6)]
        [InlineData("sa5|system1|unit2", 6)]
        [InlineData("eb5|system1|unit2")]
        [InlineData("eb5.0001|system1|unit2")]
        public async Task GivenAQuantitySearchParameterWithQuantity_WhenSearched_ThenCorrectBundleShouldBeReturned(string queryValue, params int[] expectedIndices)
        {
            Bundle bundle = await Client.SearchAsync(ResourceType.Observation, $"value-quantity={queryValue}");

            Observation[] expected = expectedIndices.Select(i => Fixture.Observations[i]).ToArray();

            ValidateBundle(bundle, expected);
        }
    }
}
