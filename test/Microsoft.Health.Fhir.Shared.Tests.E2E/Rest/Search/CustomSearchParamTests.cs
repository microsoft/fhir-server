// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Core.Extensions;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Test.Utilities;
using Xunit;
using Xunit.Abstractions;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Search
{
    [CollectionDefinition(Categories.CustomSearch, DisableParallelization = true)]
    [Collection(Categories.CustomSearch)]
    [Trait(Traits.Category, Categories.CustomSearch)]
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.Json)]
    public class CustomSearchParamTests : SearchTestsBase<HttpIntegrationTestFixture>
    {
        private readonly HttpIntegrationTestFixture _fixture;
        private ITestOutputHelper _output;

        public CustomSearchParamTests(HttpIntegrationTestFixture fixture, ITestOutputHelper output)
            : base(fixture)
        {
            _fixture = fixture;
            _output = output;
        }

        [SkippableFact]
        public async Task GivenANewSearchParam_WhenReindexingComplete_ThenResourcesSearchedWithNewParamReturned()
        {
            Skip.If(true);
            var randomName = Guid.NewGuid().ToString().ComputeHash().Substring(0, 14).ToLower();
            var searchParam = Samples.GetJsonSample<SearchParameter>("SearchParameter-AppointmentStatus");
            searchParam.Name = randomName;
            searchParam.Url = searchParam.Url.Replace("foo", randomName);
            searchParam.Code = randomName + "Code";

            // POST a new appointment
            var appointment = Samples.GetJsonSample<Appointment>("Appointment");
            appointment.Status = Appointment.AppointmentStatus.Noshow;
            var tag = new Coding(null, randomName);
            appointment.Meta = new Meta();
            appointment.Meta.Tag.Add(tag);
            FhirResponse<Appointment> expectedAppointment = await Client.CreateAsync(appointment);

            // POST a second appointment to show it is filtered and not returned when using the new search parameter
            var appointment2 = Samples.GetJsonSample<Appointment>("Appointment");
            appointment2.Status = Appointment.AppointmentStatus.Booked;
            appointment2.Meta = new Meta();
            appointment2.Meta.Tag.Add(tag);
            await Client.CreateAsync(appointment2);

            // POST a new Search parameter
            FhirResponse<SearchParameter> searchParamPosted = null;
            try
            {
                searchParamPosted = await Client.CreateAsync(searchParam);
                _output.WriteLine($"SearchParameter is posted {searchParam.Url}");

                Uri reindexJobUri;

                // Start a reindex job
                (_, reindexJobUri) = await Client.PostReindexJobAsync(new Parameters());

                await WaitForReindexStatus(reindexJobUri, "Completed");

                FhirResponse<Parameters> reindexJobResult = await Client.CheckReindexAsync(reindexJobUri);
                Parameters.ParameterComponent param = reindexJobResult.Resource.Parameter.FirstOrDefault(p => p.Name == "searchParams");
                _output.WriteLine("ReindexJobDocument:");
                var serializer = new FhirJsonSerializer();
                _output.WriteLine(serializer.SerializeToString(reindexJobResult.Resource));

                Assert.Contains(searchParamPosted.Resource.Url, param.Value.ToString());

                reindexJobResult = await WaitForReindexStatus(reindexJobUri, "Completed");
                _output.WriteLine($"Reindex job is completed, it should have reindexed the resources with {randomName}");

                var floatParse = float.TryParse(
                    reindexJobResult.Resource.Parameter.FirstOrDefault(predicate => predicate.Name == "resourcesSuccessfullyReindexed").Value.ToString(),
                    out float resourcesReindexed);

                _output.WriteLine($"Reindex job is completed, {resourcesReindexed} resources Reindexed");

                Assert.True(floatParse);
                Assert.True(resourcesReindexed > 0.0);

                // When job complete, search for resources using new parameter
                // When there are multiple instances of the fhir-server running, it could take some time
                // for the search parameter/reindex updates to propogate to all instances. Hence we are
                // adding some retries below to account for that delay.
                int retryCount = 0;
                bool success = true;
                do
                {
                    success = true;
                    retryCount++;
                    try
                    {
                        await ExecuteAndValidateBundle(
                            $"Appointment?{searchParam.Code}={Appointment.AppointmentStatus.Noshow.ToString().ToLower()}&_tag={tag.Code}",
                            expectedAppointment.Resource);
                    }
                    catch (Exception ex)
                    {
                        _output.WriteLine($"Failed to validate bundle: {ex}");
                        success = false;
                        await Task.Delay(10000);
                    }
                }
                while (!success && retryCount < 3);

                Assert.True(success);
            }
            catch (FhirException ex) when (ex.StatusCode == HttpStatusCode.BadRequest && ex.Message.Contains("not enabled"))
            {
                Skip.If(!_fixture.IsUsingInProcTestServer, "Reindex is not enabled on this server.");
                return;
            }
            catch (Exception e)
            {
                _output.WriteLine($"Exception: {e.Message}");
                _output.WriteLine($"Stack Trace: {e.StackTrace}");
                throw;
            }
            finally
            {
                // Clean up new SearchParameter
                await DeleteSearchParameterAndVerify(searchParamPosted?.Resource);
            }
        }

        [SkippableFact]
        public async Task GivenASearchParam_WhenUpdatingParam_ThenResourcesIndexedWithUpdatedParam()
        {
            Skip.If(true);
            var randomName = Guid.NewGuid().ToString().ComputeHash().Substring(28).ToLower();
            var patient = new Patient { Name = new List<HumanName> { new HumanName { Family = randomName } } };
            var searchParam = Samples.GetJsonSample<SearchParameter>("SearchParameter");
            searchParam.Name = randomName;
            searchParam.Url = searchParam.Url.Replace("foo", randomName);
            searchParam.Code = randomName;
            searchParam.Id = randomName;

            // POST a new patient
            FhirResponse<Patient> expectedPatient = await Client.CreateAsync(patient);

            // POST a new Search parameter
            FhirResponse<SearchParameter> searchParamPosted = null;
            try
            {
                searchParamPosted = await Client.CreateAsync(searchParam);

                // now update the new search parameter
                var randomNameUpdated = randomName + "U";
                searchParamPosted.Resource.Name = randomNameUpdated;
                searchParamPosted.Resource.Url = "http://hl7.org/fhir/SearchParameter/Patient-" + randomNameUpdated;
                searchParamPosted.Resource.Code = randomNameUpdated;
                searchParamPosted = await Client.UpdateAsync(searchParamPosted.Resource);

                Uri reindexJobUri;
                FhirResponse<Parameters> reindexJobResult;

                // Reindex just a single patient, so we can try searching with a partially indexed search param
                (reindexJobResult, reindexJobUri) = await Client.PostReindexJobAsync(new Parameters(), $"Patient/{expectedPatient.Resource.Id}/");
                Parameters.ParameterComponent param = reindexJobResult.Resource.Parameter.FirstOrDefault(p => p.Name == randomNameUpdated);

                if (param == null)
                {
                    _output.WriteLine($"Parameter with name equal to randomly generated name of this test case: {randomNameUpdated} not found in reindex result.");
                }

                Assert.NotNull(param);
                Assert.Equal(randomName, param.Value.ToString());

                // When job complete, search for resources using new parameter
                await ExecuteAndValidateBundle(
                            $"Patient?{searchParamPosted.Resource.Code}:exact={randomName}",
                            Tuple.Create("x-ms-use-partial-indices", "true"),
                            expectedPatient.Resource);
            }
            catch (FhirException ex) when (ex.StatusCode == HttpStatusCode.BadRequest && ex.Message.Contains("not enabled"))
            {
                Skip.If(!_fixture.IsUsingInProcTestServer, "Reindex is not enabled on this server.");
                return;
            }
            catch (Exception e)
            {
                _output.WriteLine($"Exception: {e.Message}");
                _output.WriteLine($"Stack Trace: {e.StackTrace}");
                throw;
            }
            finally
            {
                // Clean up new SearchParameter
                await DeleteSearchParameterAndVerify(searchParamPosted?.Resource);
            }
        }

        [Theory(Skip = "true")]
        [InlineData("SearchParameterBadSyntax", "A search parameter with the same code value 'diagnosis' already exists for base type 'Encounter'")]
        [InlineData("SearchParameterExpressionWrongProperty", "not supported")]
        [InlineData("SearchParameterInvalidBase", "Literal 'foo' is not a valid value for enumeration 'ResourceType'")]
        [InlineData("SearchParameterInvalidType", "Literal 'foo' is not a valid value for enumeration 'SearchParamType'")]
        [InlineData("SearchParameterMissingBase", "cardinality is 1")]
        [InlineData("SearchParameterMissingExpression", "not supported")]
        [InlineData("SearchParameterMissingType", "cardinality 1 cannot be null")]
        [InlineData("SearchParameterUnsupportedType", "not supported")]
        public async Task GivenAnInvalidSearchParam_WhenCreatingParam_ThenMeaningfulErrorReturned(string searchParamFile, string errorMessage)
        {
            var searchParam = Samples.GetJson(searchParamFile);

            try
            {
                await Client.PostAsync("SearchParameter", searchParam);
            }
            catch (FhirException ex)
            {
                Assert.Contains(ex.OperationOutcome.Issue, i => i.Diagnostics.Contains(errorMessage));
            }
        }

        private async Task<FhirResponse<Parameters>> WaitForReindexStatus(System.Uri reindexJobUri, params string[] desiredStatus)
        {
            int checkReindexCount = 0;
            string currentStatus;
            FhirResponse<Parameters> reindexJobResult = null;
            do
            {
                reindexJobResult = await Client.CheckReindexAsync(reindexJobUri);
                currentStatus = reindexJobResult.Resource.Parameter.FirstOrDefault(p => p.Name == "status")?.Value.ToString();
                checkReindexCount++;
                await Task.Delay(1000);
            }
            while (!desiredStatus.Contains(currentStatus) && checkReindexCount < 20);

            return reindexJobResult;
        }

        private async Task DeleteSearchParameterAndVerify(SearchParameter searchParam)
        {
            if (searchParam != null)
            {
                await Client.DeleteAsync(searchParam);
                var ex = await Assert.ThrowsAsync<FhirException>(() => Client.ReadAsync<SearchParameter>(ResourceType.SearchParameter, searchParam.Id));
                Assert.Contains("Gone", ex.Message);
            }
        }
    }
}
