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
    public class UriSearchTests : SearchTestsBase<UriSearchTestFixture>
    {
        public UriSearchTests(UriSearchTestFixture fixture)
            : base(fixture)
        {
        }

        [Theory]
        [InlineData("", "http://somewhere.com/test/system", 0)]
        [InlineData("", "http://somewhere.COM/test/system")]
        [InlineData("", "urn://localhost/test", 1)]
        [InlineData(":above", "system", 0)]
        [InlineData(":above", "test")]
        [InlineData(":above", "urn://localhost/test")]
        [InlineData(":below", "http", 0)]
        [InlineData(":below", "test")]
        [InlineData(":below", "urn")]
        public async Task GivenAUriSearchParam_WhenSearched_ThenCorrectBundleShouldBeReturned(string modifier, string queryValue, params int[] expectedIndices)
        {
            Bundle bundle = await Client.SearchAsync(ResourceType.ValueSet, $"url{modifier}={queryValue}");

            ValueSet[] expected = expectedIndices.Select(i => Fixture.ValueSets[i]).ToArray();

            ValidateBundle(bundle, expected);
        }
    }
}
