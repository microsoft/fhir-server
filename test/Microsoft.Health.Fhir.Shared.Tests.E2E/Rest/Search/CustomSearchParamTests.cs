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
            searchParam.Code = "fooCode";

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
            catch (Exception)
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
                    throw;
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
            await ExecuteAndValidateBundle($"Patient?{searchParam.Code}:exact={patientName}", expectedPatient.Resource);

            // Clean up new SearchParameter
            await DeleteSearchParameterAndVerify(searchParamPosted.Resource);
        }

        [Fact]
        public async Task GivenASearchParam_WhenUpdatingParam_ThenResourcesIndexedWithUpdatedParam()
        {
            var patientName = Guid.NewGuid().ToString().ComputeHash().Substring(28).ToLower();
            var patient = new Patient { Name = new List<HumanName> { new HumanName { Family = patientName } } };
            var searchParam = Samples.GetJsonSample<SearchParameter>("SearchParameter");

            // POST a new patient
            FhirResponse<Patient> expectedPatient = await Client.CreateAsync(patient);

            // POST a new Search parameter
            FhirResponse<SearchParameter> searchParamPosted = null;
            try
            {
                searchParamPosted = await Client.CreateAsync(searchParam);
            }
            catch (FhirException)
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
                    throw;
                }
            }

            // now update the new search parameter
            searchParamPosted.Resource.Name = "foo2";
            searchParamPosted.Resource.Url = "http://hl7.org/fhir/SearchParameter/Patient-foo2";
            searchParamPosted.Resource.Code = "foo2";
            searchParamPosted = await Client.UpdateAsync(searchParamPosted.Resource);

            Uri reindexJobUri;
            FhirResponse<Parameters> reindexJobResult;
            try
            {
                // Reindex just a single patient, so we can try searching with a partially indexed search param
                (reindexJobResult, reindexJobUri) = await Client.PostReindexJobAsync(new Parameters(), $"Patient/{expectedPatient.Resource.Id}/");
                Parameters.ParameterComponent param = reindexJobResult.Resource.Parameter.FirstOrDefault(p => p.Name == "foo2");

                Assert.Equal(patientName, param.Value.ToString());
            }
            catch (FhirException ex) when (ex.StatusCode == HttpStatusCode.BadRequest && ex.Message.Contains("not enabled"))
            {
                Skip.If(!_fixture.IsUsingInProcTestServer, "Reindex is not enabled on this server.");
                return;
            }

            // When job complete, search for resources using new parameter
            await ExecuteAndValidateBundle($"Patient?foo2:exact={patientName}", Tuple.Create("x-ms-use-partial-indices", "true"), expectedPatient.Resource);

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
