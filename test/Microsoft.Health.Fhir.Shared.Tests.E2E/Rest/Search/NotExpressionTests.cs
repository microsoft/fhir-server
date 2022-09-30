// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Test.Utilities;
using Xunit;
using ResourceType = Hl7.Fhir.Model.ResourceType;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Search
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer, Format.Json)]
    public sealed class NotExpressionTests : ChainingSortAndSearchValidationTestFixture
    {
        public NotExpressionTests(HttpIntegrationTestFixture fixture)
            : base(fixture)
        {
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenANotExpressionPattern_WhenSearched_ThenCompareResponseWithExpectedResults()
        {
            IReadOnlyList<HealthRecordIdentifier> healthRecordIdentifiers = await GetHealthRecordIdentifiersAsync(CancellationToken.None);

            var practitionerRoles = healthRecordIdentifiers.Where(i => i.ResourceType == ResourceType.PractitionerRole.ToString());
            foreach (HealthRecordIdentifier practitioner in practitionerRoles)
            {
                string query1 = $"_id={practitioner.Id}&active:not=false";
                Bundle queryResult1 = await Client.SearchAsync(ResourceType.PractitionerRole, query1);
                Assert.Single(queryResult1.Entry);
                Assert.Equal(practitioner.Id, queryResult1.Entry.Single().Resource.Id);

                string query2 = $"_id={practitioner.Id}&active:not=false&location:missing=false";
                Bundle queryResult2 = await Client.SearchAsync(ResourceType.PractitionerRole, query2);
                Assert.Single(queryResult2.Entry);
                Assert.Equal(practitioner.Id, queryResult2.Entry.Single().Resource.Id);

                string query3 = $"_id={practitioner.Id}&active:not=true";
                Bundle queryResult3 = await Client.SearchAsync(ResourceType.PractitionerRole, query3);
                Assert.Empty(queryResult3.Entry);
            }
        }
    }
}
