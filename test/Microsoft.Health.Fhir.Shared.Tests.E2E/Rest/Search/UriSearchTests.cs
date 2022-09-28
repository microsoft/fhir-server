// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
using System.Web;
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
    public class UriSearchTests : SearchTestsBase<UriSearchTestFixture>
    {
        public UriSearchTests(UriSearchTestFixture fixture)
            : base(fixture)
        {
        }

        [Theory]
        [InlineData("", "http://somewhere.com/test/system", 0)]
        [InlineData("", "http://somewhere.COM/test/system")]
        [InlineData("", "http://example.org/rdf#54135-9", 2)]
        [InlineData("", "urn://localhost/test", 1)]
        [InlineData(":above", "http://somewhere.com/test/system/123", 0)]
        [InlineData(":above", "test")]
        [InlineData(":above", "urn://localhost/test")]
        [InlineData(":above", "http://example.org/rdf#54135-9-9-10", 2, 3)]
        [InlineData(":below", "http", 0, 2, 3)]
        [InlineData(":below", "test")]
        [InlineData(":below", "urn")]
        public async Task GivenAUriSearchParam_WhenSearched_ThenCorrectBundleShouldBeReturned(string modifier, string queryValue, params int[] expectedIndices)
        {
            Bundle bundle = await Client.SearchAsync(ResourceType.ValueSet, $"url{modifier}={HttpUtility.UrlEncode(queryValue)}&_tag={Fixture.FixtureTag}");

            ValueSet[] expected = expectedIndices.Select(i => Fixture.ValueSets[i]).ToArray();

            ValidateBundle(bundle, expected);
        }
    }
}
