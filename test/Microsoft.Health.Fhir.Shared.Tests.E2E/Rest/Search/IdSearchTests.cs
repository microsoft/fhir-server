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
    public sealed class IdSearchTests : ChainingSortAndSearchValidationTestFixture
    {
        public IdSearchTests(HttpIntegrationTestFixture fixture)
            : base(fixture)
        {
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAnIdExpressionPattern_WhenSearched_ThenCompareResponseWithExpectedResults()
        {
            IReadOnlyList<HealthRecordIdentifier> healthRecordIdentifiers = await GetHealthRecordIdentifiersAsync(CancellationToken.None);

            var healthCareServices = healthRecordIdentifiers.Where(i => i.ResourceType == ResourceType.HealthcareService.ToString());
            foreach (HealthRecordIdentifier healthCareService in healthCareServices)
            {
                string query1 = $"location:missing=false&_id={healthCareService.Id}";
                Bundle queryResult1 = await Client.SearchAsync(ResourceType.HealthcareService, query1);
                Assert.Single(queryResult1.Entry);
                Assert.Equal(healthCareService.Id, queryResult1.Entry.Single().Resource.Id);

                string query2 = $"_id={healthCareService.Id}";
                Bundle queryResult2 = await Client.SearchAsync(ResourceType.HealthcareService, query2);
                Assert.Single(queryResult2.Entry);
                Assert.Equal(healthCareService.Id, queryResult2.Entry.Single().Resource.Id);
            }
        }
    }
}
