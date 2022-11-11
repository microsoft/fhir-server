// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
using System.Web;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Import
{
    [Trait(Traits.OwningTeam, OwningTeam.FhirImport)]
    [Trait(Traits.Category, Categories.Import)]
    [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer, Format.Json)]
    public class ImportUriSearchTests : IClassFixture<ImportUriSearchTestFixture>
    {
        private readonly TestFhirClient _client;
        private readonly ImportUriSearchTestFixture _fixture;

        public ImportUriSearchTests(ImportUriSearchTestFixture fixture)
        {
            _client = fixture.TestFhirClient;
            _fixture = fixture;
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
            Bundle bundle = await _client.SearchAsync(ResourceType.ValueSet, $"url{modifier}={HttpUtility.UrlEncode(queryValue)}&_tag={_fixture.FixtureTag}");

            ValueSet[] expected = expectedIndices.Select(i => _fixture.ValueSets[i]).ToArray();

            ImportTestHelper.VerifyBundle(bundle, expected);
        }
    }
}
