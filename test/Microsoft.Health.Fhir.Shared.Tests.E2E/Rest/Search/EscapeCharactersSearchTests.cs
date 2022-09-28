// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Shared.Tests;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Test.Utilities;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Search
{
    [Trait(Traits.Category, Categories.Search)]
    [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer, Format.Json)]
    public class EscapeCharactersSearchTests : ChainingSortAndSearchValidationTestFixture
    {
        public EscapeCharactersSearchTests(HttpIntegrationTestFixture fixture)
            : base(fixture)
        {
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAnUrlWithEscapingCharacters1_WhenSearched_ThenCompareResponseWithExpectedResults()
        {
            IReadOnlyList<HealthRecordIdentifier> healthRecordIdentifiers = await GetHealthRecordIdentifiersAsync(CancellationToken.None);

            var collection = healthRecordIdentifiers.Where(i => i.ResourceType == ResourceType.HealthcareService.ToString());

            foreach (HealthRecordIdentifier item in collection)
            {
                string query1 = $"_id={item.Id}&name:missing=false&active:not=false&location:missing=false&_has:PractitionerRole:service:active=true";
                Bundle queryResult1 = await Client.SearchAsync(ResourceType.HealthcareService, query1);

                string query2 = HttpFhirUtility.EncodeUrl(query1);
                Bundle queryResult2 = await Client.SearchAsync(ResourceType.HealthcareService, query2);

                Assert.Equal(queryResult1.Entry.Count, queryResult2.Entry.Count);
                for (int i = 0; i < queryResult1.Entry.Count; i++)
                {
                    Assert.Equal(queryResult1.Entry[i].Resource.Id, queryResult2.Entry[i].Resource.Id);
                }
            }
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAnUrlWithEscapingCharacters2_WhenSearched_ThenCompareResponseWithExpectedResults()
        {
            IReadOnlyList<Bundle.EntryComponent> entries = await GetHealthEntryComponentsAsync(CancellationToken.None);

            var collection = entries.Where(i => i.Resource.TypeName == ResourceType.PractitionerRole.ToString());

            // Extracting the IDs of practitioners from practitioner roles.
            var practitionerIds = collection
                .Select(e => e.Resource as PractitionerRole)
                .Select(p => p.Practitioner.Reference)
                .Select(r => r.Substring(r.IndexOf("/") + 1));

            foreach (string id in practitionerIds)
            {
                string query1 = $"name:missing=false&_has:PractitionerRole:service:practitioner={id}&active:not=false&location:missing=false&_has:PractitionerRole:service:active=true";
                Bundle queryResult1 = await Client.SearchAsync(ResourceType.HealthcareService, query1);

                string query2 = HttpFhirUtility.EncodeUrl(query1);
                Bundle queryResult2 = await Client.SearchAsync(ResourceType.HealthcareService, query2);

                Assert.Equal(queryResult1.Entry.Count, queryResult2.Entry.Count);
                for (int i = 0; i < queryResult1.Entry.Count; i++)
                {
                    Assert.Equal(queryResult1.Entry[i].Resource.Id, queryResult2.Entry[i].Resource.Id);
                }
            }
        }
    }
}
