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
    [Trait("Traits.OwningTeam", OwningTeam.FhirImport)]
    [Trait(Traits.Category, Categories.Import)]
    [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer, Format.Json)]
    public class ImportReferenceSearchTests : IClassFixture<ImportReferenceSearchTestFixture>
    {
        private readonly TestFhirClient _client;
        private readonly ImportReferenceSearchTestFixture _fixture;

        public ImportReferenceSearchTests(ImportReferenceSearchTestFixture fixture)
        {
            _client = fixture.TestFhirClient;
            _fixture = fixture;
        }

        [Theory]
        [InlineData("organization=Organization/123", 0)]
        [InlineData("organization=123", 0)]
        [InlineData("organization=Organization/1")]
        [InlineData("organization=organization/123")]
        [InlineData("organization=Organization/ijk", 2)] // This is specified in the resource as "ijk", without the type, but the type can only be Organization
        [InlineData("organization=ijk", 2)]
        [InlineData("general-practitioner=Practitioner/p1", 3)]
        [InlineData("general-practitioner:Practitioner=Practitioner/p1", 3)]
        [InlineData("general-practitioner:Practitioner=p1", 3)]
        [InlineData("general-practitioner=Practitioner/p2")] // This is specified in the resource as "p2", without the type, but because the parameter can reference several types and we don't resolve references, this search does not succeed
        [InlineData("general-practitioner:Practitioner=p2")] // This is specified in the resource as "p2", without the type, but because the parameter can reference several types and we don't resolve references, this search does not succeed
        [InlineData("general-practitioner=p2", 4)]
        public async Task GivenAReferenceSearchParam_WhenSearched_ThenCorrectBundleShouldBeReturned(string query, params int[] matchIndices)
        {
            Bundle bundle = await _client.SearchAsync(ResourceType.Patient, $"{query}&_tag={_fixture.FixtureTag}");

            Patient[] expected = matchIndices.Select(i => _fixture.Patients[i]).ToArray();

            ImportTestHelper.VerifyBundle(bundle, expected);
        }
    }
}
