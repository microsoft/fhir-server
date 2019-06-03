// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Search
{
    [HttpIntegrationFixtureArgumentSets(DataStore.CosmosDb, Format.Json, FhirVersion.All)]
    public class ReferenceSearchTests : SearchTestsBase<ReferenceSearchTestFixture>
    {
        public ReferenceSearchTests(ReferenceSearchTestFixture fixture)
            : base(fixture)
        {
        }

        [Theory]
        [InlineData("Organization/123", 0)]
        [InlineData("Organization/1")]
        [InlineData("organization/123")]
        public async Task GivenAReferenceSearchParam_WhenSearched_ThenCorrectBundleShouldBeReturned(string valueToSearch, params int[] matchIndices)
        {
            string query = $"organization={valueToSearch}";

            ResourceElement bundle = await Client.SearchAsync("Patient", query);

            ResourceElement[] expected = matchIndices.Select(i => Fixture.Patients[i]).ToArray();

            ValidateBundle(bundle, expected);
        }
    }
}
