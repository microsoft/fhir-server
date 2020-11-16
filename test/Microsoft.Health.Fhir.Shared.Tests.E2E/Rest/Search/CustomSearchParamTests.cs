// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Search
{
    [HttpIntegrationFixtureArgumentSets(DataStore.CosmosDb, Format.Json)]
    public class CustomSearchParamTests : SearchTestsBase<HttpIntegrationTestFixture>
    {
        public CustomSearchParamTests(HttpIntegrationTestFixture fixture)
            : base(fixture)
        {
        }

        [Fact]
        public async Task GivenANewSearchParam_WhenReindexingComplete_ThenResourcesSearchedWithNewParamReturned()
        {
            var patientName = System.Guid.NewGuid().ToString().Substring(28).ToLower();
            var patient = new Patient() { Name = new List<HumanName>() { new HumanName() { Family = patientName } } };
            var searchParam = Samples.GetJsonSample<SearchParameter>("SearchParameter");

            // POST a new patient
            var expectedPatient = await Client.CreateAsync<Patient>(patient);

            // POST a second patient to show it is filtered and not retuend when using the new search parameter
            await Client.CreateAsync<Patient>(Samples.GetJsonSample<Patient>("Patient"));

            // POST a new Search parameter
            await Client.CreateAsync<SearchParameter>(searchParam);

            // Start a reindex job
            (var reindexJobResult, var reindexJobUri) = await Client.PostReindexJobAsync(new Parameters());

            await WaitForReindexStatus(reindexJobUri, "Running", "Completed");

            reindexJobResult = await Client.CheckReindexAsync(reindexJobUri);
            var param = reindexJobResult.Resource.Parameter.Where(p => p.Name == "searchParams").FirstOrDefault();

            Assert.Equal("http://hl7.org/fhir/SearchParameter/Patient-foo", param.Value.ToString());

            await WaitForReindexStatus(reindexJobUri, "Completed");

            // When job complete, search for resources using new parameter
            await ExecuteAndValidateBundle($"Patient?foo={patientName}", expectedPatient.Resource);
        }

        private async Task WaitForReindexStatus(System.Uri reindexJobUri, params string[] desiredStatus)
        {
            int checkReindexCount = 0;
            var currentStatus = string.Empty;
            do
            {
                var reindexJobResult = await Client.CheckReindexAsync(reindexJobUri);
                currentStatus = reindexJobResult.Resource.Parameter.Where(p => p.Name == "status").FirstOrDefault().Value.ToString();
                checkReindexCount++;
                await Task.Delay(1000);
            }
            while (!desiredStatus.Contains(currentStatus) && checkReindexCount < 20);
        }
    }
}
