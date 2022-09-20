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
using Microsoft.Health.Fhir.Core.Features.Operations;
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
    public class CustomSearchParamTests : SearchTestsBase<HttpIntegrationTestFixture>, IAsyncLifetime
    {
        private readonly HttpIntegrationTestFixture _fixture;
        private ITestOutputHelper _output;
        private const int MaxRetryCount = 10;

        public CustomSearchParamTests(HttpIntegrationTestFixture fixture, ITestOutputHelper output)
            : base(fixture)
        {
            _fixture = fixture;
            _output = output;
        }

        public async Task InitializeAsync()
        {
            await Client.DeleteAllResources(ResourceType.Specimen, null);
            await Client.DeleteAllResources(ResourceType.Immunization, null);
        }

        [SkippableFact]
        public async Task GivenANewSearchParam_WhenReindexingComplete_ThenResourcesSearchedWithNewParamReturned()
        {
            var randomName = Guid.NewGuid().ToString().ComputeHash().Substring(0, 14).ToLower();
            var searchParam = Samples.GetJsonSample<SearchParameter>("SearchParameter-SpecimenStatus");
            searchParam.Name = randomName;
            searchParam.Url = searchParam.Url.Replace("foo", randomName);
            searchParam.Code = randomName + "Code";

            // POST a new Specimen
            var specimen = Samples.GetJsonSample<Specimen>("Specimen");
            specimen.Status = Specimen.SpecimenStatus.Available;
            var tag = new Coding(null, randomName);
            specimen.Meta = new Meta();
            specimen.Meta.Tag.Add(tag);
            FhirResponse<Specimen> expectedSpecimen = await Client.CreateAsync(specimen);

            // POST a second Specimen to show it is filtered and not returned when using the new search parameter
            var specimen2 = Samples.GetJsonSample<Specimen>("Specimen");
            specimen2.Status = Specimen.SpecimenStatus.EnteredInError;
            specimen2.Meta = new Meta();
            specimen2.Meta.Tag.Add(tag);
            await Client.CreateAsync(specimen2);

            // POST a new Search parameter
            FhirResponse<SearchParameter> searchParamPosted = null;
            try
            {
                searchParamPosted = await Client.CreateAsync(searchParam);
                _output.WriteLine($"SearchParameter is posted {searchParam.Url}");

                // Start a reindex job
                Uri reindexJobUri;
                FhirResponse<Parameters> reindexJobResult;

                (reindexJobResult, reindexJobUri) = await RunReindexToCompletion(new Parameters());
                _output.WriteLine($"Reindex job is completed, it should have reindexed the resources with {randomName}");

                Parameters.ParameterComponent param = reindexJobResult.Resource.Parameter.FirstOrDefault(p => p.Name == JobRecordProperties.SearchParams);
                Assert.Contains(searchParamPosted.Resource.Url, param?.Value?.ToString());

                // When job complete, search for resources using new parameter
                // When there are multiple instances of the fhir-server running, it could take some time
                // for the search parameter/reindex updates to propogate to all instances. Hence we are
                // adding some retries below to account for that delay.
                int retryCount = 0;
                bool success = true;
                await Task.Delay(TimeSpan.FromSeconds(20));

                do
                {
                    success = true;
                    retryCount++;
                    try
                    {
                        await ExecuteAndValidateBundle(
                            $"Specimen?{searchParam.Code}={Specimen.SpecimenStatus.Available.ToString().ToLower()}&_tag={tag.Code}",
                            expectedSpecimen.Resource);
                    }
                    catch (Exception ex)
                    {
                        string error = $"Attempt {retryCount} of {MaxRetryCount}: Failed to validate bundle: {ex}";
                        _output.WriteLine(error);
                        success = false;
                        await Task.Delay(TimeSpan.FromSeconds(10));
                    }
                }
                while (!success && retryCount < MaxRetryCount);

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
            var randomName = Guid.NewGuid().ToString().ComputeHash().Substring(28).ToLower();
            var patient = new Patient { Name = new List<HumanName> { new HumanName { Family = randomName } } };
            var searchParam = Samples.GetJsonSample<SearchParameter>("SearchParameter-Patient-foo");
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
                (reindexJobResult, reindexJobUri) = await RunReindexToCompletion(new Parameters(), $"Patient/{expectedPatient.Resource.Id}/");

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

        [Theory]
        [InlineData("SearchParameterBadSyntax", "A search parameter with the same code value 'diagnosis' already exists for base type 'Encounter'")]
        [InlineData("SearchParameterExpressionWrongProperty", "Can't find 'Encounter.diagnosis.foo' in type 'Encounter'")]
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

        [SkippableFact]
        public async Task GivenASearchParameterWithMultipleBaseResourceTypes_WhenTargetingReindexJobToResourceType_ThenOnlyTargetedTypesAreReindexed()
        {
            var randomName = Guid.NewGuid().ToString().ComputeHash().Substring(0, 14).ToLower();
            var searchParam = Samples.GetJsonSample<SearchParameter>("SearchParameter-Resource-idfoo");
            searchParam.Name = searchParam.Name.Replace("foo", randomName);
            searchParam.Url = searchParam.Url.Replace("foo", randomName);
            searchParam.Code = randomName + "Code";

            // POST a new Specimen
            var specimen = Samples.GetJsonSample<Specimen>("Specimen");
            FhirResponse<Specimen> expectedSpecimen = await Client.CreateAsync(specimen);
            _output.WriteLine($"{nameof(expectedSpecimen)} Response.StatusCode is {expectedSpecimen.Response.StatusCode}");

            // POST a second Specimen to show it is filtered and not returned when using the new search parameter
            var specimen2 = Samples.GetJsonSample<Specimen>("Specimen");
            await Client.CreateAsync(specimen2);

            // POST a new patient
            var patient = new Patient { Name = new List<HumanName> { new HumanName { Family = randomName } } };
            FhirResponse<Patient> expectedPatient = await Client.CreateAsync(patient);
            _output.WriteLine($"{nameof(expectedPatient)} Response.StatusCode is {expectedPatient.Response.StatusCode}");

            // POST a new Search parameter
            FhirResponse<SearchParameter> searchParamPosted = null;
            bool success = true;
            try
            {
                searchParamPosted = await Client.CreateAsync(searchParam);
                _output.WriteLine($"{nameof(searchParamPosted)} Response.StatusCode is {searchParamPosted.Response.StatusCode} and posted Url is {searchParam.Url}");

                Uri reindexJobUri;
                FhirResponse<Parameters> reindexJobResult;

                // Start a reindex job
                var reindexParameters = new Parameters();
                reindexParameters.Add("targetResourceTypes", new FhirString("Specimen"));
                (reindexJobResult, reindexJobUri) = await RunReindexToCompletion(reindexParameters);

                Parameters.ParameterComponent searchParamListParam = reindexJobResult.Resource.Parameter.FirstOrDefault(p => p.Name == JobRecordProperties.SearchParams);
                Parameters.ParameterComponent targetResourcesParam = reindexJobResult.Resource.Parameter.FirstOrDefault(p => p.Name == JobRecordProperties.TargetResourceTypes);
                Parameters.ParameterComponent resourcesParam = reindexJobResult.Resource.Parameter.FirstOrDefault(p => p.Name == JobRecordProperties.Resources);

                Assert.Contains(searchParamPosted.Resource.Url, searchParamListParam?.Value?.ToString());
                Assert.Equal("Specimen", targetResourcesParam?.Value?.ToString());
                Assert.Equal("Specimen", resourcesParam?.Value?.ToString());

                _output.WriteLine($"Reindex job is completed, it should have reindexed the resources of type Specimen only.");

                // When job complete, search for resources using new parameter
                // When there are multiple instances of the fhir-server running, it could take some time
                // for the search parameter/reindex updates to propogate to all instances. Hence we are
                // adding some retries below to account for that delay.
                int retryCount = 0;
                await Task.Delay(TimeSpan.FromSeconds(20));

                do
                {
                    success = true;
                    retryCount++;
                    try
                    {
                        await ExecuteAndValidateBundle(
                            $"Specimen?{searchParam.Code}={expectedSpecimen.Resource.Id}",
                            Tuple.Create("x-ms-use-partial-indices", "true"),
                            expectedSpecimen.Resource);

                        _output.WriteLine($"Success on {retryCount} of {MaxRetryCount}");
                    }
                    catch (Exception ex)
                    {
                        _output.WriteLine($"Failed to validate bundle: {ex}");
                        success = false;
                        await Task.Delay(TimeSpan.FromSeconds(10));
                    }

                    // now searching for patient with same search parameter should not work
                    var searchUrl = $"Patient?{searchParam.Code}={expectedPatient.Resource.Id}";
                    Bundle bundle = await Client.SearchAsync(searchUrl, Tuple.Create("x-ms-use-partial-indices", "true"));
                    Assert.Empty(bundle.Entry);

                    // finally searching with new SearchParam but without partial header should not use
                    // new search parameter, because it should not be marked fully reindexed
                    searchUrl = $"Patient?{searchParam.Code}={expectedPatient.Resource.Id}";
                    bundle = await Client.SearchAsync(searchUrl);
                    Assert.DoesNotContain(searchParam.Code, bundle.SelfLink.ToString());
                }
                while (!success && retryCount < MaxRetryCount);

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
                if (success)
                {
                    await DeleteSearchParameterAndVerify(searchParamPosted?.Resource);
                }
                else
                {
                    _output.WriteLine($"Test was not successful, check db for search params");
                }
            }
        }

        [SkippableFact]
        public async Task GivenASearchParameterWithMultipleBaseResourceTypes_WhenTargetingReindexJobToSameListOfResourceTypes_ThenSearchParametersMarkedFullyIndexed()
        {
            var randomName = Guid.NewGuid().ToString().ComputeHash().Substring(0, 14).ToLower();
            var searchParam = Samples.GetJsonSample<SearchParameter>("SearchParameter-Resource-idfoo");
            searchParam.Name = searchParam.Name.Replace("foo", randomName);
            searchParam.Url = searchParam.Url.Replace("foo", randomName);
            searchParam.Code = randomName + "Code";
            searchParam.Base = new List<ResourceType?>() { ResourceType.Specimen, ResourceType.Immunization };

            // POST a new Specimen
            var specimen = Samples.GetJsonSample<Specimen>("Specimen");
            FhirResponse<Specimen> expectedSpecimen = await Client.CreateAsync(specimen);
            _output.WriteLine($"{nameof(expectedSpecimen)} Response.StatusCode is {expectedSpecimen.Response.StatusCode}");

            // POST a second Specimen to show it is filtered and not returned when using the new search parameter
            var specimen2 = Samples.GetJsonSample<Specimen>("Specimen");
            await Client.CreateAsync(specimen2);

            // POST a new Immunization
            var immunization = Samples.GetJsonSample<Immunization>("Immunization");
            FhirResponse<Immunization> expectedImmunization = await Client.CreateAsync(immunization);
            _output.WriteLine($"{nameof(expectedImmunization)} Response.StatusCode is {expectedImmunization.Response.StatusCode}");

            // POST a new Search parameter
            FhirResponse<SearchParameter> searchParamPosted = null;
            bool success = true;
            try
            {
                searchParamPosted = await Client.CreateAsync(searchParam);
                _output.WriteLine($"{nameof(searchParamPosted)} Response.StatusCode is {searchParamPosted.Response.StatusCode} and posted Url is {searchParam.Url}");

                Uri reindexJobUri;
                FhirResponse<Parameters> reindexJobResult;

                // Start a reindex job
                var reindexParameters = new Parameters();
                reindexParameters.Add("targetResourceTypes", new FhirString("Specimen,Immunization"));
                (reindexJobResult, reindexJobUri) = await RunReindexToCompletion(reindexParameters);

                Parameters.ParameterComponent searchParamListParam = reindexJobResult.Resource.Parameter.FirstOrDefault(p => p.Name == JobRecordProperties.SearchParams);
                Parameters.ParameterComponent targetResourcesParam = reindexJobResult.Resource.Parameter.FirstOrDefault(p => p.Name == JobRecordProperties.TargetResourceTypes);
                Parameters.ParameterComponent resourcesParam = reindexJobResult.Resource.Parameter.FirstOrDefault(p => p.Name == JobRecordProperties.Resources);

                Assert.Contains(searchParamPosted.Resource.Url, searchParamListParam?.Value?.ToString());
                Assert.Contains("Specimen", targetResourcesParam?.Value?.ToString());
                Assert.Contains("Specimen", resourcesParam?.Value?.ToString());
                Assert.Contains("Immunization", targetResourcesParam?.Value?.ToString());
                Assert.Contains("Immunization", resourcesParam?.Value?.ToString());

                _output.WriteLine($"Reindex job is completed, it should have reindexed the resources of type Specimen and Immunization only.");

                // When job complete, search for resources using new parameter
                // When there are multiple instances of the fhir-server running, it could take some time
                // for the search parameter/reindex updates to propogate to all instances. Hence we are
                // adding some retries below to account for that delay.
                int retryCount = 0;
                await Task.Delay(TimeSpan.FromSeconds(20));

                do
                {
                    success = true;
                    retryCount++;

                    try
                    {
                        await ExecuteAndValidateBundle(
                            $"Specimen?{searchParam.Code}={expectedSpecimen.Resource.Id}",
                            expectedSpecimen.Resource);

                        await ExecuteAndValidateBundle(
                            $"Immunization?{searchParam.Code}={expectedImmunization.Resource.Id}",
                            expectedImmunization.Resource);

                        _output.WriteLine($"Success on {retryCount} of {MaxRetryCount}");
                    }
                    catch (Exception ex)
                    {
                        string error = $"Attempt {retryCount} of {MaxRetryCount}: Failed to validate bundle: {ex}";

                        _output.WriteLine(error);
                        success = false;
                        await Task.Delay(TimeSpan.FromSeconds(10));
                    }
                }
                while (!success && retryCount < MaxRetryCount);

                Assert.True(success, $"There are bundle validation failures. {retryCount} attempts reached. Check test logs.");
            }
            catch (FhirException ex) when (ex.StatusCode == HttpStatusCode.BadRequest && ex.Message.Contains("not enabled"))
            {
                _output.WriteLine($"Skipping because reindex is disabled.");
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
                if (success)
                {
                    await DeleteSearchParameterAndVerify(searchParamPosted?.Resource);
                }
                else
                {
                    _output.WriteLine($"Test was not successful, check db for search params");
                }
            }
        }

        [Fact]
        public async Task GivenNonParametersRequestBody_WhenReindexSent_ThenBadRequest()
        {
            string body = Samples.GetJson("PatientWithMinimalData");
            var ex = await Assert.ThrowsAsync<FhirException>(async () => await Client.PostAsync("$reindex", body));
            Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        }

        private async Task<(FhirResponse<Parameters> response, Uri uri)> RunReindexToCompletion(Parameters reindexParameters, string uniqueResource = null)
        {
            Uri reindexJobUri;
            FhirResponse<Parameters> response;
            (response, reindexJobUri) = await Client.PostReindexJobAsync(reindexParameters, uniqueResource);

            // check for status code reindexJobResult
            if (reindexJobUri != null)
            {
                await WaitForReindexStatus(reindexJobUri, "Completed");

                response = await Client.CheckReindexAsync(reindexJobUri);

                _output.WriteLine("ReindexJobDocument:");
                var serializer = new FhirJsonSerializer();
                _output.WriteLine(serializer.SerializeToString(response.Resource));

                var floatParse = float.TryParse(
                    response.Resource.Parameter.FirstOrDefault(p => p.Name == JobRecordProperties.ResourcesSuccessfullyReindexed).Value.ToString(),
                    out float resourcesReindexed);

                _output.WriteLine($"Reindex job is completed, {resourcesReindexed} resources Reindexed");

                Assert.True(floatParse);
                Assert.True(resourcesReindexed > 0.0);
            }

            _output.WriteLine("response.Resource.Parameter output:");
            foreach (var p in response.Resource.Parameter)
            {
                _output.WriteLine($"  {p.Name}: {p.Value}");
            }

            return (response, reindexJobUri);
        }

        private async Task<FhirResponse<Parameters>> WaitForReindexStatus(Uri reindexJobUri, params string[] desiredStatus)
        {
            int checkReindexCount = 0;
            string currentStatus;
            FhirResponse<Parameters> reindexJobResult = null;
            do
            {
                reindexJobResult = await Client.CheckReindexAsync(reindexJobUri);
                currentStatus = reindexJobResult.Resource.Parameter.FirstOrDefault(p => p.Name == JobRecordProperties.Status)?.Value.ToString();
                checkReindexCount++;
                await Task.Delay(1000);
            }
            while (!desiredStatus.Contains(currentStatus) && checkReindexCount < 20);

            if (checkReindexCount >= 20)
            {
                throw new Exception("ReindexJob did not complete within 20 seconds.");
            }

            return reindexJobResult;
        }

        private async Task DeleteSearchParameterAndVerify(SearchParameter searchParam)
        {
            if (searchParam != null)
            {
                // Clean up new SearchParameter
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
                        await Client.DeleteAsync(searchParam);
                    }
                    catch (Exception exp)
                    {
                        _output.WriteLine($"Attempt {retryCount} of {MaxRetryCount}: CustomSearchParameter test experienced issue attempted to clean up SearchParameter {searchParam.Url}.  The exception is {exp}");
                        var fhirException = exp as FhirException;
                        if (fhirException != null && fhirException.OperationOutcome?.Issue != null)
                        {
                            foreach (var issue in fhirException.OperationOutcome.Issue)
                            {
                                _output.WriteLine("FhirException OperationOutome message from trying to delete SearchParameter is CustomSearchParam test: {0}", issue.Diagnostics);
                            }
                        }

                        success = false;
                        await Task.Delay(10000);
                    }
                }
                while (!success && retryCount < MaxRetryCount);

                Assert.True(success);
                var ex = await Assert.ThrowsAsync<FhirException>(() => Client.ReadAsync<SearchParameter>(ResourceType.SearchParameter, searchParam.Id));
                Assert.Contains("Gone", ex.Message);
            }
        }

        public Task DisposeAsync() => Task.CompletedTask;
    }
}
