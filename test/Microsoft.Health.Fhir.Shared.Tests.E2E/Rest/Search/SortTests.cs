// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Microsoft.Health.Fhir.Tests.E2E.Rest;
using Microsoft.Health.Fhir.Tests.E2E.Rest.Search;
using Xunit;
using Xunit.Sdk;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Search
{
    [HttpIntegrationFixtureArgumentSets(DataStore.CosmosDb, Format.Json)]
    public class SortTests : SearchTestsBase<HttpIntegrationTestFixture>
    {
        public SortTests(HttpIntegrationTestFixture fixture)
            : base(fixture)
        {
        }

        [Fact]
        public async Task GivenResources_WhenSearchedWithUnsupportedSortParams_ThenSortIsDroppedFromUrl()
        {
            var tag = Guid.NewGuid().ToString();
            var patients = await CreateResources(tag);

            await Assert.ThrowsAsync<FhirException>(async () => await ExecuteAndValidateBundle($"Patient?_tag={tag}&_sort=name", false, patients.Cast<Resource>().ToArray()));
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenResources_WhenSearchedWithSortParams_ThenResourcesAreReturnedInTheCorrectOrder()
        {
            var tag = Guid.NewGuid().ToString();
            var patients = await CreateResources(tag);

            await ExecuteAndValidateBundle($"Patient?_tag={tag}&_sort=_lastUpdated", false, patients.Cast<Resource>().ToArray());
            await ExecuteAndValidateBundle($"Patient?_tag={tag}&_sort=-_lastUpdated", false, patients.Reverse().Cast<Resource>().ToArray());

            await Assert.ThrowsAsync<CollectionException>(async () => await ExecuteAndValidateBundle($"Patient?_tag={tag}&_sort=_lastUpdated", false, patients.Reverse().Cast<Resource>().ToArray()));
        }

        private async Task<Patient[]> CreateResources(string tag)
        {
            // Create various resources.
            Patient[] patients = await Client.CreateResourcesAsync<Patient>(
                p => SetPatientInfo(p, "Seattle", "Robinson", tag),
                p => SetPatientInfo(p, "Portland", "Williamas", tag),
                p => SetPatientInfo(p, "Seattle", "Jones", tag));

            return patients;
        }

        private void SetPatientInfo(Patient patient, string city, string family, string tag)
        {
            patient.Meta = new Meta { Tag = new List<Coding> { new Coding(null, tag) }, };

            patient.Address = new List<Address>
            {
                new Address
                {
                    City = city,
                },
            };

            patient.Name = new List<HumanName> { new HumanName { Family = family }, };
        }
    }
}
