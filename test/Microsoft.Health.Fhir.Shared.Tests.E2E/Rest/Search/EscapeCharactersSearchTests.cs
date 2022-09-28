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
                .Select(r => r.Substring(r.IndexOf("/") + 1))
                .Distinct();

            foreach (string id in practitionerIds)
            {
                string query1 = $"name:missing=false&_has:PractitionerRole:service:practitioner={id}&active:not=false&location:missing=false&_has:PractitionerRole:service:active=true&_total=accurate";
                Bundle queryResult1 = await Client.SearchAsync(ResourceType.HealthcareService, query1);

                string query2 = HttpFhirUtility.EncodeUrl(query1);
                Bundle queryResult2 = await Client.SearchAsync(ResourceType.HealthcareService, query2);

                Assert.Equal(queryResult1.Entry.Count, queryResult2.Entry.Count);
                Assert.Equal(queryResult1.Total, queryResult2.Total);

                for (int i = 0; i < queryResult1.Entry.Count; i++)
                {
                    Assert.Equal(queryResult1.Entry[i].Resource.Id, queryResult2.Entry[i].Resource.Id);
                }

                Assert.Equal(queryResult1.Link.Count, queryResult2.Link.Count);

                if (queryResult1.NextLink != null)
                {
                    // "Next links" URIs are already encoded.
                    Assert.NotNull(queryResult1.NextLink);
                    Assert.NotNull(queryResult2.NextLink);

                    Bundle nextQueryResult1 = await Client.SearchAsync(ResourceType.HealthcareService, queryResult1.NextLink.Query.Substring(1));
                    Bundle nextQueryResult2 = await Client.SearchAsync(ResourceType.HealthcareService, queryResult2.NextLink.Query.Substring(1));

                    Assert.Equal(nextQueryResult1.Entry.Count, nextQueryResult2.Entry.Count);

                    for (int i = 0; i < nextQueryResult1.Entry.Count; i++)
                    {
                        Assert.Equal(nextQueryResult1.Entry[i].Resource.Id, nextQueryResult2.Entry[i].Resource.Id);
                    }
                }
            }
        }
    }
}
