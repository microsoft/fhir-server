// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Test.Utilities;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Search
{
    [Trait("Traits.OwningTeam", OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.Json)]
    public class NumberSearchTests : SearchTestsBase<NumberSearchTestFixture>
    {
        public NumberSearchTests(NumberSearchTestFixture fixture)
            : base(fixture)
        {
        }

        [Theory]
        [InlineData("3")]
        [InlineData("5", 2)]
        [InlineData("5.000", 2)]
        [InlineData("eq3")]
        [InlineData("eq5", 2)]
        [InlineData("eq5.000", 2)]
        [InlineData("ne5", 0, 1, 3, 4)]
        [InlineData("ne5.000", 0, 1, 3, 4)]
        [InlineData("lt4.9", 0, 1)]
        [InlineData("lt5", 0, 1)]
        [InlineData("lt5.000", 0, 1)]
        [InlineData("lt5.01", 0, 1, 2)]
        [InlineData("gt4.9", 2, 3, 4)]
        [InlineData("gt5", 3, 4)]
        [InlineData("gt5.000", 3, 4)]
        [InlineData("gt5.1", 3, 4)]
        [InlineData("le4.9", 0, 1)]
        [InlineData("le5", 0, 1, 2)]
        [InlineData("le5.000", 0, 1, 2)]
        [InlineData("le5.0001", 0, 1, 2)]
        [InlineData("ge4.9999", 2, 3, 4)]
        [InlineData("ge5", 2, 3, 4)]
        [InlineData("ge5.000", 2, 3, 4)]
        [InlineData("ge5.001", 3, 4)]
        [InlineData("sa4.9999", 2, 3, 4)]
        [InlineData("sa5", 3, 4)]
        [InlineData("eb5", 0, 1)]
        [InlineData("eb5.0001", 0, 1, 2)]
        public async Task GivenANumberSearchParam_WhenSearched_ThenCorrectBundleShouldBeReturned(string queryValue, params int[] expectedIndices)
        {
            Bundle bundle = await Client.SearchAsync(ResourceType.RiskAssessment, $"_tag={Fixture.Tag}&probability={queryValue}");

            RiskAssessment[] expected = expectedIndices.Select(i => Fixture.RiskAssessments[i]).ToArray();

            ValidateBundle(bundle, expected);
        }
    }
}
