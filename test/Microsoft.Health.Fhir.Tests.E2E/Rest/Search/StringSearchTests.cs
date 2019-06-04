// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Threading.Tasks;
using Hl7.Fhir.ElementModel;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Search
{
    [HttpIntegrationFixtureArgumentSets(DataStore.CosmosDb, Format.Json, FhirVersion.All)]
    public class StringSearchTests : SearchTestsBase<StringSearchTestFixture>
    {
        public StringSearchTests(StringSearchTestFixture fixture)
            : base(fixture)
        {
        }

        [Theory]
        [Trait(Traits.Priority, Priority.One)]
        [InlineData("", "seattle", true)]
        [InlineData("", "SEATTLE", true)]
        [InlineData("", "Seattle", true)]
        [InlineData("", "Sea", true)]
        [InlineData("", "sea", true)]
        [InlineData("", "123", false)]
        [InlineData(":exact", "Seattle", true)]
        [InlineData(":exact", "seattle", false)]
        [InlineData(":exact", "SEATTLE", false)]
        [InlineData(":exact", "Sea", false)]
        [InlineData(":contains", "att", true)]
        [InlineData(":contains", "EAT", true)]
        [InlineData(":contains", "123", false)]
        public async Task GivenAStringSearchParam_WhenSearched_ThenCorrectBundleShouldBeReturned(string modifier, string valueToSearch, bool shouldMatch)
        {
            string query = $"address-city{modifier}={valueToSearch}";

            ResourceElement bundle = await Client.SearchAsync(KnownResourceTypes.Patient, query);

            Assert.NotNull(bundle);

            ResourceElement expectedPatient = Fixture.Patients[0];

            if (shouldMatch)
            {
                IEnumerable<ITypedElement> bundleEntries = bundle.Select(KnownFhirPaths.BundleEntries);
                Assert.NotEmpty(bundleEntries);
                Assert.Collection(
                    bundleEntries,
                    e => Assert.True(Client.Compare(expectedPatient, e)));
            }
            else
            {
                Assert.Empty(bundle.Select(KnownFhirPaths.BundleEntries));
            }
        }

        [Fact]
        public async Task GivenAStringSearchParamWithMultipleValues_WhenSearched_ThenCorrectBundleShouldBeReturned()
        {
            ResourceElement bundle = await Client.SearchAsync(KnownResourceTypes.Patient, "family=Smith,Ander");

            ValidateBundle(bundle, Fixture.Patients[0], Fixture.Patients[2]);
        }
    }
}
