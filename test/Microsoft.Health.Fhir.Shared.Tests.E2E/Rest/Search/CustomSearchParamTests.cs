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
using Microsoft.Health.Fhir.Core.Features;
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
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    [Trait(Traits.Category, Categories.CustomSearch)]
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.Json)]
    public class CustomSearchParamTests : SearchTestsBase<HttpIntegrationTestFixture>, IAsyncLifetime
    {
        private readonly HttpIntegrationTestFixture _fixture;
        private readonly ITestOutputHelper _output;
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
            Skip.If(!_fixture.IsUsingInProcTestServer, "Reindex is not enabled on this server.");

            var randomName = Guid.NewGuid().ToString().ComputeHash()[..14].ToLower();
            SearchParameter searchParam = Samples.GetJsonSample<SearchParameter>("SearchParameter-SpecimenStatus");
            searchParam.Name = randomName;
            searchParam.Url = searchParam.Url.Replace("foo", randomName);
            searchParam.Code = randomName + "Code";

            // POST a new Specimen
            Specimen specimen = Samples.GetJsonSample<Specimen>("Specimen");
            specimen.Status = Specimen.SpecimenStatus.Available;
            var tag = new Coding(null, randomName);
            specimen.Meta = new Meta();
            specimen.Meta.Tag.Add(tag);
            FhirResponse<Specimen> expectedSpecimen = await Client.CreateAsync(specimen);

            // POST a second Specimen to show it is filtered and not returned when using the new search parameter
            Specimen specimen2 = Samples.GetJsonSample<Specimen>("Specimen");
            specimen2.Status = Specimen.SpecimenStatus.EnteredInError;
            specimen2.Meta = new Meta();
            specimen2.Meta.Tag.Add(tag);
            await Client.CreateAsync(specimen2);

            // POST a new Search parameter
            FhirResponse<SearchParameter> searchParamPosted = null;
            int retryCount = 0;
            bool success = true;
            try
            {
                searchParamPosted = await Client.CreateAsync(searchParam);
                _output.WriteLine($"SearchParameter is posted {searchParam.Url}");

                // Start a reindex job
                Uri reindexJobUri;
                FhirResponse<Parameters> reindexJobResult;
                (reindexJobResult, reindexJobUri) = await RunReindexToCompletion(new Parameters());

                Parameters.ParameterComponent param = reindexJobResult.Resource.Parameter.FirstOrDefault(p => p.Name == JobRecordProperties.SearchParams);
                Assert.Contains(searchParamPosted.Resource.Url, param?.Value?.ToString());

                do
                {
                    success = true;
                    retryCount++;
                    try
                    {
                        await ExecuteAndValidateBundle(
                            $"Specimen?{searchParam.Code}={Specimen.SpecimenStatus.Available.ToString().ToLower()}&_tag={tag.Code}",
                            expectedSpecimen.Resource);

                        _output.WriteLine($"Success on attempt {retryCount} of {MaxRetryCount}");
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
            catch (FhirClientException ex) when (ex.StatusCode == HttpStatusCode.BadRequest && ex.Message.Contains("not enabled"))
            {
                _output.WriteLine($"Skipping because reindex is disabled.");
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
            Skip.If(!_fixture.IsUsingInProcTestServer, "Reindex is not enabled on this server.");

            var randomName = Guid.NewGuid().ToString().ComputeHash()[28..].ToLower();
            var patient = new Patient { Name = new List<HumanName> { new HumanName { Family = randomName } } };
            SearchParameter searchParam = Samples.GetJsonSample<SearchParameter>("SearchParameter-Patient-foo");
            searchParam.Name = randomName;
            searchParam.Url = searchParam.Url.Replace("foo", randomName);
            searchParam.Code = randomName;
            searchParam.Id = randomName;

            // POST a new patient
            FhirResponse<Patient> expectedPatient = await Client.CreateAsync(patient);

            // POST a new Search parameter
            FhirResponse<SearchParameter> searchParamPosted = null;
            int retryCount = 0;
            bool success = true;
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

                do
                {
                    success = true;
                    retryCount++;
                    try
                    {
                        // When reindex job complete, search for resources using new parameter
                        await ExecuteAndValidateBundle(
                                    $"Patient?{searchParamPosted.Resource.Code}:exact={randomName}",
                                    Tuple.Create(KnownHeaders.PartiallyIndexedParamsHeaderName, "true"),
                                    expectedPatient.Resource);

                        _output.WriteLine($"Success on attempt {retryCount} of {MaxRetryCount}");
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
            catch (FhirClientException ex) when (ex.StatusCode == HttpStatusCode.BadRequest && ex.Message.Contains("not enabled"))
            {
                _output.WriteLine($"Skipping because reindex is disabled.");
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
        [InlineData("SearchParameterBadSyntax", "The search parameter definition contains one or more invalid entries.")]
#if Stu3 || R4 || R4B
        [InlineData("SearchParameterInvalidBase", "Literal 'foo' is not a valid value for enumeration 'ResourceType'")]
#else
        [InlineData("SearchParameterInvalidBase", "Literal 'foo' is not a valid value for enumeration 'VersionIndependentResourceTypesAll'")]
#endif
        [InlineData("SearchParameterExpressionWrongProperty", "Can't find 'Encounter.diagnosis.foo' in type 'Encounter'")]
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
            catch (FhirClientException ex)
            {
                Assert.Contains(ex.OperationOutcome.Issue, i => i.Diagnostics.Contains(errorMessage));
            }
        }

        [Fact]
        public async Task GivenNonParametersRequestBody_WhenReindexSent_ThenBadRequest()
        {
            string body = Samples.GetJson("PatientWithMinimalData");
            FhirClientException ex = await Assert.ThrowsAsync<FhirClientException>(async () => await Client.PostAsync("$reindex", body));
            Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        }

        private async Task<(FhirResponse<Parameters> response, Uri uri)> RunReindexToCompletion(Parameters reindexParameters, string uniqueResource = null)
        {
            Uri reindexJobUri;
            FhirResponse<Parameters> response;
            (response, reindexJobUri) = await Client.PostReindexJobAsync(reindexParameters, uniqueResource);

            // this becomes null when the uniqueResource gets passed in
            if (reindexJobUri != null)
            {
                response = await Client.WaitForReindexStatus(reindexJobUri, "Completed");

                _output.WriteLine("ReindexJobDocument:");
                var serializer = new FhirJsonSerializer();
                serializer.Settings.Pretty = true;
                _output.WriteLine(serializer.SerializeToString(response.Resource));

                var floatParse = float.TryParse(
                    response.Resource.Parameter.FirstOrDefault(p => p.Name == JobRecordProperties.ResourcesSuccessfullyReindexed).Value.ToString(),
                    out float resourcesReindexed);

                Assert.True(floatParse);
                Assert.True(resourcesReindexed > 0.0);
            }
            else
            {
                _output.WriteLine("response.Resource.Parameter output:");
                foreach (Parameters.ParameterComponent paramComponent in response.Resource.Parameter)
                {
                    _output.WriteLine($"  {paramComponent.Name}: {paramComponent.Value}");
                }
            }

            return (response, reindexJobUri);
        }

        private async Task DeleteSearchParameterAndVerify(SearchParameter searchParam)
        {
            if (searchParam != null)
            {
                // Clean up new SearchParameter
                // When there are multiple instances of the fhir-server running, it could take some time
                // for the search parameter/reindex updates to propagate to all instances. Hence we are
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
                        if (exp is FhirClientException fhirException && fhirException.OperationOutcome?.Issue != null)
                        {
                            foreach (OperationOutcome.IssueComponent issueComponent in fhirException.OperationOutcome.Issue)
                            {
                                _output.WriteLine("FhirException OperationOutome message from trying to delete SearchParameter is CustomSearchParam test: {0}", issueComponent.Diagnostics);
                            }
                        }

                        success = false;
                        await Task.Delay(TimeSpan.FromSeconds(10));
                    }
                }
                while (!success && retryCount < MaxRetryCount);

                Assert.True(success);
                FhirClientException ex = await Assert.ThrowsAsync<FhirClientException>(() => Client.ReadAsync<SearchParameter>(ResourceType.SearchParameter, searchParam.Id));
                Assert.Contains("Gone", ex.Message);
            }
        }

        public Task DisposeAsync() => Task.CompletedTask;
    }
}
