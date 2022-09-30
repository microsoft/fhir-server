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
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.Json)]
    public class ReferenceSearchTests : SearchTestsBase<ReferenceSearchTestFixture>
    {
        public ReferenceSearchTests(ReferenceSearchTestFixture fixture)
            : base(fixture)
        {
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
            Bundle bundle = await Client.SearchAsync(ResourceType.Patient, query + $"&_tag={Fixture.Tag}");

            Patient[] expected = matchIndices.Select(i => Fixture.Patients[i]).ToArray();

            ValidateBundle(bundle, expected);
        }
    }
}
