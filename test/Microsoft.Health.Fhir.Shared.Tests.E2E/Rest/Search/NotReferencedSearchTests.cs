// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Net;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Test.Utilities;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Search
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer, Format.Json)]
    public class NotReferencedSearchTests : SearchTestsBase<NotReferencedSearchTestFixture>
    {
        public NotReferencedSearchTests(NotReferencedSearchTestFixture fixture)
            : base(fixture)
        {
        }

        [Fact]
        public async Task GivenANotReferencedSearchParameter_WhenSearched_ThenOnlyResourcesWithNoReferencesAreReturned()
        {
            try
            {
                Bundle bundle = await Client.SearchAsync(ResourceType.Patient, $"_not-referenced=*:*&_tag={Fixture.Tag}");

                Patient[] expected = Fixture.Patients.Where(patient => !Fixture.Observation.Subject.Reference.Contains(patient.Id, StringComparison.OrdinalIgnoreCase)).ToArray();

                ValidateBundle(bundle, expected);
            }
            catch (FhirClientException fce)
            {
                Assert.Fail($"A non-expected '{nameof(FhirClientException)}' was raised. Url: {Client.HttpClient.BaseAddress}. Activity Id: {fce.Response.GetRequestId()}. Error: {fce.Message}");
            }
            catch (Exception e)
            {
                Assert.Fail($"A non-expected '{e.GetType()}' was raised. Url: {Client.HttpClient.BaseAddress}. No Activity Id present. Error: {e.Message}");
            }
        }
    }
}
