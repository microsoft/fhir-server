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
        public async Task GivenAnUrlWithEscapingCharacters_WhenSearched_ThenCompareResponseWithExpectedResults()
        {
            IReadOnlyList<HealthRecordIdentifier> healthRecordIdentifiers = await GetHealthRecordIdentifiersAsync(CancellationToken.None);

            var practicionerRoles = healthRecordIdentifiers.Where(i => i.ResourceType == ResourceType.PractitionerRole.ToString());

            foreach (HealthRecordIdentifier practitionerRole in practicionerRoles)
            {
                string query1 = $"name:missing=false&_has:PractitionerRole:service:practitioner={practitionerRole.Id}&active:not=false&location:missing=false&_has:PractitionerRole:service:active=true";
                Bundle queryResult1 = await Client.SearchAsync(ResourceType.HealthcareService, query1);
                ValidateBundle(queryResult1);

                string query2 = HttpFhirUtility.EncodeUrl(query1);
                Bundle queryResult2 = await Client.SearchAsync(ResourceType.HealthcareService, query2);
                ValidateBundle(queryResult2);

                Assert.Equal(queryResult1.Entry.Count, queryResult2.Entry.Count);
            }
        }
    }
}
