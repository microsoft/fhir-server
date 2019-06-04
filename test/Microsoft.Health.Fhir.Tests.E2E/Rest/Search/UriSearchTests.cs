// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Search
{
    [HttpIntegrationFixtureArgumentSets(DataStore.CosmosDb, Format.Json, FhirVersion.All)]
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
            FhirResponse<ResourceElement> bundleResponse = await Client.SearchAsync("ValueSet", $"url{modifier}={queryValue}");

            ResourceElement[] expected = expectedIndices.Select(i => Fixture.ValueSets[i]).ToArray();

            ValidateBundle(bundleResponse.Resource, expected);
        }
    }
}
