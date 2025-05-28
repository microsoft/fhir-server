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
    public class DuplicateSearchParamTests : SearchTestsBase<HttpIntegrationTestFixture>, IAsyncLifetime
    {
        private readonly HttpIntegrationTestFixture _fixture;
        private readonly ITestOutputHelper _output;
        private const int MaxRetryCount = 10;

        public DuplicateSearchParamTests(HttpIntegrationTestFixture fixture, ITestOutputHelper output)
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

        // Test cases:
        // A combination of different SearchParameters that will be rejected, one for each of POST, PUT, conditional PUTs
        // A set of different SearchParameters that will be accepted, and than marked as duplicate
            // These should NOT trigger a reindex job
        // A test of submitting SearchParameters in a bundle?
        // What about PATCH? how will that work?
        /*
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
        */

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
                    catch (FhirClientException fhirEx) when (fhirEx.StatusCode == HttpStatusCode.BadRequest && fhirEx.Message.Contains("not enabled"))
                    {
                        _output.WriteLine($"Skipping because reindex is disabled.");
                        return;
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
