// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Hl7.Fhir.Model;
using Microsoft.Health.Core.Extensions;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Test.Utilities;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Search
{
    [Trait(Traits.Category, Categories.CustomSearch)]
    [HttpIntegrationFixtureArgumentSets(DataStore.CosmosDb, Format.Json)]
    public class CustomSearchParamTests : SearchTestsBase<HttpIntegrationTestFixture>
    {
        private readonly HttpIntegrationTestFixture _fixture;

        public CustomSearchParamTests(HttpIntegrationTestFixture fixture)
            : base(fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task GivenANewSearchParam_WhenReindexingComplete_ThenResourcesSearchedWithNewParamReturned()
        {
            var patientName = Guid.NewGuid().ToString().ComputeHash().Substring(28).ToLower();
            var patient = new Patient { Name = new List<HumanName> { new HumanName { Family = patientName } } };
            var searchParam = Samples.GetJsonSample<SearchParameter>("SearchParameter");

            // POST a new patient
            FhirResponse<Patient> expectedPatient = await Client.CreateAsync(patient);

            // POST a second patient to show it is filtered and not returned when using the new search parameter
            await Client.CreateAsync(Samples.GetJsonSample<Patient>("Patient"));

            // POST a new Search parameter
            FhirResponse<SearchParameter> searchParamPosted = null;
            try
            {
                searchParamPosted = await Client.CreateAsync(searchParam);
            }
            catch (Exception ex)
            {
                // if the SearchParameter exists, we should delete it and recreate it
                var searchParamBundle = await Client.SearchAsync(ResourceType.SearchParameter, $"url={searchParam.Url}");
                if (searchParamBundle.Resource?.Entry[0] != null && searchParamBundle.Resource?.Entry[0].Resource.ResourceType == ResourceType.SearchParameter)
                {
                    await DeleteSearchParameterAndVerify(searchParamBundle.Resource?.Entry[0].Resource as SearchParameter);
                    searchParamPosted = await Client.CreateAsync(searchParam);
                }
                else
                {
                    throw ex;
                }
            }

            Uri reindexJobUri;
            try
            {
                // Start a reindex job
                (_, reindexJobUri) = await Client.PostReindexJobAsync(new Parameters());
            }
            catch (FhirException ex) when (ex.StatusCode == HttpStatusCode.BadRequest && ex.Message.Contains("not enabled"))
            {
                Skip.If(!_fixture.IsUsingInProcTestServer, "Reindex is not enabled on this server.");
                return;
            }

            await WaitForReindexStatus(reindexJobUri, "Running", "Completed");

            FhirResponse<Parameters> reindexJobResult = await Client.CheckReindexAsync(reindexJobUri);
            Parameters.ParameterComponent param = reindexJobResult.Resource.Parameter.FirstOrDefault(p => p.Name == "searchParams");

            Assert.Contains("http://hl7.org/fhir/SearchParameter/Patient-foo", param.Value.ToString());

            await WaitForReindexStatus(reindexJobUri, "Completed");

            // When job complete, search for resources using new parameter
            await ExecuteAndValidateBundle($"Patient?foo:exact={patientName}", expectedPatient.Resource);

            // Clean up new SearchParameter
            await DeleteSearchParameterAndVerify(searchParamPosted.Resource);
        }

        private async Task WaitForReindexStatus(System.Uri reindexJobUri, params string[] desiredStatus)
        {
            int checkReindexCount = 0;
            string currentStatus;
            do
            {
                FhirResponse<Parameters> reindexJobResult = await Client.CheckReindexAsync(reindexJobUri);
                currentStatus = reindexJobResult.Resource.Parameter.FirstOrDefault(p => p.Name == "status")?.Value.ToString();
                checkReindexCount++;
                await Task.Delay(1000);
            }
            while (!desiredStatus.Contains(currentStatus) && checkReindexCount < 20);
        }

        private async Task DeleteSearchParameterAndVerify(SearchParameter searchParam)
        {
            await Client.DeleteAsync(searchParam);
            var ex = await Assert.ThrowsAsync<FhirException>(() => Client.ReadAsync<SearchParameter>(ResourceType.SearchParameter, searchParam.Id));
            Assert.Contains("Gone", ex.Message);
        }
    }
}
