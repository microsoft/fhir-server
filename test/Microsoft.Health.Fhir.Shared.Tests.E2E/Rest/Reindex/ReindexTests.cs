// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Test.Utilities;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Reindex
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.IndexAndReindex)]
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.Json)]
    public class ReindexTests : IClassFixture<ReindexTestFixture>
    {
        private readonly ReindexTestFixture _fixture;

        public ReindexTests(ReindexTestFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task GivenReindexJobWithMixedZeroAndNonZeroCountResources_WhenReindexCompletes_ThenSearchParametersShouldWork()
        {
            // Scenario 1: Test that search parameter status is only updated when ALL jobs complete successfully
            // This tests both zero-count and non-zero-count resource scenarios
            var mixedBaseSearchParam = new SearchParameter();
            var randomSuffix = Guid.NewGuid().ToString("N").Substring(0, 8);
            var testResources = new List<(string resourceType, string resourceId)>();

            try
            {
                // Create test data - Specimen has resources, Immunization has zero count
                var specimen = await CreateSpecimenResourceAsync($"specimen-{randomSuffix}", "Test Specimen");
                Assert.NotNull(specimen);
                testResources.Add(("Specimen", specimen.Id));

                // Create a single search parameter that applies to BOTH Specimen and Immunization
                // This allows us to test the scenario where one resource type has data and another has none
                mixedBaseSearchParam = await CreateCustomSearchParameterAsync(
                    $"custom-mixed-base-{randomSuffix}",
                    ["Specimen", "Immunization"],  // Applies to both resource types
                    "Specimen.type",  // Valid for Specimen
                    SearchParamType.Token);
                Assert.NotNull(mixedBaseSearchParam);

                // Create reindex job targeting the mixed-base search parameter
                var parameters = new Parameters
                {
                    Parameter = new List<Parameters.ParameterComponent>(),
                };

                var (response, jobUri) = await _fixture.TestFhirClient.PostReindexJobAsync(parameters);

                Assert.Equal(HttpStatusCode.Created, response.Response.StatusCode);
                Assert.NotNull(jobUri);

                // Wait for job to complete
                var jobStatus = await WaitForJobCompletionAsync(jobUri, TimeSpan.FromSeconds(240));
                Assert.True(
                    jobStatus == OperationStatus.Completed,
                    $"Expected Completed, got {jobStatus}");

                // Verify search parameter was indexed
                var paramAfter = await _fixture.TestFhirClient.ReadAsync<SearchParameter>($"SearchParameter/{mixedBaseSearchParam.Id}");
                Assert.NotNull(paramAfter);
                Assert.NotNull(paramAfter.Resource);

                // Verify search parameter is working for Specimen (which has data)
                // We expect the specimen record to be returned
                await VerifySearchParameterIsWorkingAsync(
                    $"Specimen?{mixedBaseSearchParam.Code}=119295008",
                    mixedBaseSearchParam.Code,
                    expectedResourceType: "Specimen",
                    shouldFindRecords: true);

                // Verify search parameter is working for Immunization (which has no data)
                // We expect no immunization records to be returned (empty result set)
                await VerifySearchParameterIsWorkingAsync(
                    $"Immunization?{mixedBaseSearchParam.Code}=207",
                    mixedBaseSearchParam.Code,
                    expectedResourceType: "Immunization",
                    shouldFindRecords: false);
            }
            finally
            {
                // Cleanup all test data including resources and search parameters
                await CleanupTestDataAsync(testResources, mixedBaseSearchParam);
            }
        }

        [Fact]
        public async Task GivenReindexJobWithResourceAndAddedAfterSingleCustomSearchParameterAndBeforeReindex_WhenReindexCompletes_ThenSearchParameterShouldWork()
        {
            // Scenario 2: Test that search parameter with invalid expression fails indexing
            // This validates that indexing failures prevent status updates
            var randomSuffix = Guid.NewGuid().ToString("N").Substring(0, 8);
            var specimenSearchParam = new SearchParameter();
            var immunizationSearchParam = new SearchParameter();
            var testResources = new List<(string resourceType, string resourceId)>();

            try
            {
                // Create custom search parameters - one with valid FHIRPath expression
                specimenSearchParam = await CreateCustomSearchParameterAsync($"custom-parameter-before-specimen-{randomSuffix}", ["Specimen"], "Specimen.type", SearchParamType.Token);

                // Create test resources that will be indexed
                var specimen = await CreateSpecimenResourceAsync($"specimen-after-{randomSuffix}", "AfterParameter Specimen");
                Assert.NotNull(specimen);
                testResources.Add(("Specimen", specimen.Id));

                // Create reindex job targeting both search parameters
                var parameters = new Parameters
                {
                    Parameter = new List<Parameters.ParameterComponent>(),
                };

                var (response, jobUri) = await _fixture.TestFhirClient.PostReindexJobAsync(parameters);

                Assert.Equal(HttpStatusCode.Created, response.Response.StatusCode);
                Assert.NotNull(jobUri);

                // Wait for job to complete
                var jobStatus = await WaitForJobCompletionAsync(jobUri, TimeSpan.FromSeconds(240));
                Assert.True(
                    jobStatus == OperationStatus.Completed,
                    $"Expected Completed, got {jobStatus}");

                // Verify search parameters were retrieved after reindex
                var specimenParamAfter = await _fixture.TestFhirClient.ReadAsync<SearchParameter>($"SearchParameter/{specimenSearchParam.Id}");

                Assert.NotNull(specimenParamAfter);
                Assert.NotNull(specimenParamAfter.Resource);

                // The valid search parameter should still be usable
                await VerifySearchParameterIsWorkingAsync($"Specimen?{specimenSearchParam.Code}=119295008", specimenSearchParam.Code, "Specimen", true);
            }
            finally
            {
                // Cleanup all test data including resources and search parameters
                await CleanupTestDataAsync(testResources, specimenSearchParam, immunizationSearchParam);
            }
        }

        [Fact]
        public async Task GivenReindexJobWithResourceAndAddedAfterMultiCustomSearchParameterAndBeforeReindex_WhenReindexCompletes_ThenSearchParametersShouldWork()
        {
            // Scenario 2: Test that search parameter with invalid expression fails indexing
            // This validates that indexing failures prevent status updates
            var randomSuffix = Guid.NewGuid().ToString("N").Substring(0, 8);
            var specimenSearchParam = new SearchParameter();
            var immunizationSearchParam = new SearchParameter();
            var testResources = new List<(string resourceType, string resourceId)>();

            try
            {
                // Create custom search parameters - one with valid FHIRPath expression
                specimenSearchParam = await CreateCustomSearchParameterAsync($"custom-parameter-before-specimen-{randomSuffix}", ["Specimen"], "Specimen.type", SearchParamType.Token);
                immunizationSearchParam = await CreateCustomSearchParameterAsync($"custom-parameter-before-imm-{randomSuffix}", ["Immunization"], "Immunization.vaccineCode", SearchParamType.Token);

                // Create test resources that will be indexed
                var specimen = await CreateSpecimenResourceAsync($"specimen-after-{randomSuffix}", "AfterParameter Specimen");
                Assert.NotNull(specimen);
                testResources.Add(("Specimen", specimen.Id));

                // Create reindex job targeting both search parameters
                var parameters = new Parameters
                {
                    Parameter = new List<Parameters.ParameterComponent>(),
                };

                var (response, jobUri) = await _fixture.TestFhirClient.PostReindexJobAsync(parameters);

                Assert.Equal(HttpStatusCode.Created, response.Response.StatusCode);
                Assert.NotNull(jobUri);

                // Wait for job to complete
                var jobStatus = await WaitForJobCompletionAsync(jobUri, TimeSpan.FromSeconds(240));
                Assert.True(
                    jobStatus == OperationStatus.Completed,
                    $"Expected Completed, got {jobStatus}");

                // The valid search parameter should still be usable
                await VerifySearchParameterIsWorkingAsync($"Specimen?{specimenSearchParam.Code}=119295008", specimenSearchParam.Code, "Specimen", true);
            }
            finally
            {
                // Cleanup all test data including resources and search parameters
                await CleanupTestDataAsync(testResources, specimenSearchParam, immunizationSearchParam);
            }
        }

        [Fact]
        public async Task GivenReindexWithCaseVariantSearchParameterUrls_WhenBothHaveSameStatus_ThenBothShouldBeProcessedCorrectly()
        {
            // Scenario 3a: Case variant search parameter URLs with same status (Supported, Supported)
            // Both should be treated as separate entries and processed correctly
            var randomSuffix = Guid.NewGuid().ToString("N").Substring(0, 8);
            var lowerCaseParam = new SearchParameter();
            var upperCaseParam = new SearchParameter();
            var testResources = new List<(string resourceType, string resourceId)>();

            try
            {
                // Create test resource
                var specimen = await CreateSpecimenResourceAsync($"specimen-{randomSuffix}", "Case Test Specimen");
                Assert.NotNull(specimen);
                testResources.Add(("Specimen", specimen.Id));

                // Create search parameters with unique codes
                lowerCaseParam = await CreateCustomSearchParameterAsync($"custom-case-sensitive-{randomSuffix}", ["Specimen"], "Specimen.type", SearchParamType.Token);
                upperCaseParam = await CreateCustomSearchParameterAsync($"custom-case-Sensitive-{randomSuffix}", ["Specimen"], "Specimen.status", SearchParamType.Token);
                Assert.NotNull(lowerCaseParam);
                Assert.NotNull(upperCaseParam);

                // Create reindex job targeting both case-variant search parameters
                var parameters = new Parameters
                {
                    Parameter = new List<Parameters.ParameterComponent>(),
                };

                var (response, jobUri) = await _fixture.TestFhirClient.PostReindexJobAsync(parameters);

                Assert.Equal(HttpStatusCode.Created, response.Response.StatusCode);
                Assert.NotNull(jobUri);

                // Wait for job completion
                var jobStatus = await WaitForJobCompletionAsync(jobUri, TimeSpan.FromSeconds(120));
                Assert.True(
                    jobStatus == OperationStatus.Completed,
                    $"Expected Completed, got {jobStatus}");

                // Verify both search parameters are working after reindex
                await VerifySearchParameterIsWorkingAsync($"Specimen?{lowerCaseParam.Code}=119295008", lowerCaseParam.Code);
                await VerifySearchParameterIsWorkingAsync($"Specimen?{upperCaseParam.Code}=available", upperCaseParam.Code);
            }
            finally
            {
                // Cleanup all test data including resources and search_parameters
                await CleanupTestDataAsync(testResources, lowerCaseParam, upperCaseParam);
            }
        }

        [Fact]
        public async Task GivenReindexWithCaseVariantSearchParameterUrls_WhenHavingDifferentStatuses_ThenBothSearchParametersShouldWork()
        {
            // Scenario 3b: Case variant search parameter URLs with different statuses
            // Verify both are set to the correct status when all jobs complete
            var randomSuffix = Guid.NewGuid().ToString("N").Substring(0, 8);
            var specimenTypeParam = new SearchParameter();
            var specimenStatusParam = new SearchParameter();
            var testResources = new List<(string resourceType, string resourceId)>();

            try
            {
                // Create test resource
                var specimen = await CreateSpecimenResourceAsync($"specimen-{randomSuffix}", "Diff Status Specimen");
                Assert.NotNull(specimen);
                testResources.Add(("Specimen", specimen.Id));

                // Create custom search parameters with different expressions and unique codes
                specimenTypeParam = await CreateCustomSearchParameterAsync($"custom-diff-type-{randomSuffix}", ["Specimen"], "Specimen.type", SearchParamType.Token);
                specimenStatusParam = await CreateCustomSearchParameterAsync($"custom-diff-status-{randomSuffix}", ["Specimen"], "Specimen.status", SearchParamType.Token);
                Assert.NotNull(specimenTypeParam);
                Assert.NotNull(specimenStatusParam);

                // Create reindex job
                var parameters = new Parameters
                {
                    Parameter = new List<Parameters.ParameterComponent>(),
                };

                var (response, jobUri) = await _fixture.TestFhirClient.PostReindexJobAsync(parameters);

                Assert.Equal(HttpStatusCode.Created, response.Response.StatusCode);
                Assert.NotNull(jobUri);

                // Wait for job completion
                var jobStatus = await WaitForJobCompletionAsync(jobUri, TimeSpan.FromSeconds(120));
                Assert.True(
                    jobStatus == OperationStatus.Completed,
                    $"Expected Completed, got {jobStatus}");

                // Verify both search parameters are working after reindex
                await VerifySearchParameterIsWorkingAsync($"Specimen?{specimenTypeParam.Code}=119295008", specimenTypeParam.Code);
                await VerifySearchParameterIsWorkingAsync($"Specimen?{specimenStatusParam.Code}=available", specimenStatusParam.Code);
            }
            finally
            {
                // Cleanup all test data including resources and search parameters
                await CleanupTestDataAsync(testResources, specimenTypeParam, specimenStatusParam);
            }
        }

        [Fact]
        public async Task GivenSearchParameterAddedAndReindexed_WhenSearchParameterIsDeleted_ThenAfterReindexSearchParameterShouldNotBeSupported()
        {
            // Comprehensive lifecycle test:
            // 1. Create a Specimen record with specific data
            // 2. Add a custom search parameter
            // 3. Reindex to index the new search parameter
            // 4. Verify the search parameter works and returns the Specimen
            // 5. Delete the search parameter
            // 6. Reindex to remove the search parameter from the index
            // 7. Verify the search parameter is no longer supported (returns not-supported error)
            var randomSuffix = Guid.NewGuid().ToString("N").Substring(0, 8);
            var searchParam = new SearchParameter();
            var specimenId = string.Empty;
            var testResources = new List<(string resourceType, string resourceId)>();

            try
            {
                // Step 1: Create a Specimen with specific data that matches our search parameter expression
                var specimenType = $"LifecycleTest{randomSuffix}";
                var specimen = await CreateSpecimenResourceAsync($"specimen-lifecycle-{randomSuffix}", specimenType);
                Assert.NotNull(specimen);
                specimenId = specimen.Id;
                testResources.Add(("Specimen", specimen.Id));
                System.Diagnostics.Debug.WriteLine($"Created specimen with ID {specimen.Id} and type {specimenType}");

                // Step 2: Create a custom search parameter for Specimen.type
                searchParam = await CreateCustomSearchParameterAsync(
                    $"custom-lifecycle-{randomSuffix}",
                    ["Specimen"],
                    "Specimen.type",
                    SearchParamType.Token);
                Assert.NotNull(searchParam);
                System.Diagnostics.Debug.WriteLine($"Created search parameter with code {searchParam.Code} and ID {searchParam.Id}");

                // Step 3: Reindex to index the newly created search parameter
                var reindexParams = new Parameters
                {
                    Parameter = new List<Parameters.ParameterComponent>(),
                };

                var (reindexResponse1, reindexUri1) = await _fixture.TestFhirClient.PostReindexJobAsync(reindexParams);
                Assert.Equal(HttpStatusCode.Created, reindexResponse1.Response.StatusCode);
                Assert.NotNull(reindexUri1);
                System.Diagnostics.Debug.WriteLine("Started first reindex job to index the new search parameter");

                var jobStatus1 = await WaitForJobCompletionAsync(reindexUri1, TimeSpan.FromSeconds(240));
                Assert.True(
                    jobStatus1 == OperationStatus.Completed,
                    $"First reindex job should complete successfully, but got {jobStatus1}");
                System.Diagnostics.Debug.WriteLine("First reindex job completed successfully");

                // Step 4: Verify the search parameter works by searching for the specimen
                var searchQuery = $"Specimen?{searchParam.Code}=119295008";
                await VerifySearchParameterIsWorkingAsync(
                    searchQuery,
                    searchParam.Code,
                    expectedResourceType: "Specimen",
                    shouldFindRecords: true);

                // Step 5: Delete the search parameter
                await _fixture.TestFhirClient.DeleteAsync($"SearchParameter/{searchParam.Id}");
                System.Diagnostics.Debug.WriteLine($"Deleted search parameter {searchParam.Code} (ID: {searchParam.Id})");

                // Step 6: Reindex to remove the search parameter from the index
                var (reindexResponse2, reindexUri2) = await _fixture.TestFhirClient.PostReindexJobAsync(reindexParams);
                Assert.Equal(HttpStatusCode.Created, reindexResponse2.Response.StatusCode);
                Assert.NotNull(reindexUri2);
                System.Diagnostics.Debug.WriteLine("Started second reindex job to remove the deleted search parameter");

                var jobStatus2 = await WaitForJobCompletionAsync(reindexUri2, TimeSpan.FromSeconds(240));
                Assert.True(
                    jobStatus2 == OperationStatus.Completed,
                    $"Second reindex job should complete successfully, but got {jobStatus2}");
                System.Diagnostics.Debug.WriteLine("Second reindex job completed successfully");

                // Step 7: Verify the search parameter is no longer supported
                var postDeleteSearchResponse = await _fixture.TestFhirClient.SearchAsync(searchQuery);
                Assert.NotNull(postDeleteSearchResponse);
                System.Diagnostics.Debug.WriteLine($"Executed search query after deletion: {searchQuery}");

                // Verify that a "NotSupported" error is now present
                var hasNotSupportedErrorAfterDelete = HasNotSupportedError(postDeleteSearchResponse.Resource);

                Assert.True(
                    hasNotSupportedErrorAfterDelete,
                    $"Search parameter {searchParam.Code} should NOT be supported after deletion and reindex. Expected 'NotSupported' error in response.");
                System.Diagnostics.Debug.WriteLine($"Search parameter {searchParam.Code} correctly returns 'NotSupported' error after deletion");
            }
            finally
            {
                // Cleanup any remaining resources
                await CleanupTestDataAsync(testResources);
            }
        }

        private async Task<Patient> CreatePatientResourceAsync(string id, string name)
        {
            var patient = new Patient
            {
                Id = id,
                Name = new List<HumanName>
                {
                    new HumanName { Given = new[] { name } },
                },
            };

            try
            {
                var result = await _fixture.TestFhirClient.CreateAsync(patient);
                return result;
            }
            catch
            {
                return patient;
            }
        }

        private async Task<SearchParameter> CreateCustomSearchParameterAsync(string code, string[] baseResourceTypes, string expression, SearchParamType searchParamType = SearchParamType.String)
        {
#if R5
            var baseResourceTypeList = new List<VersionIndependentResourceTypesAll?>();
#else
            var baseResourceTypeList = new List<ResourceType?>();
#endif

            if (baseResourceTypes != null && baseResourceTypes.Length > 0)
            {
                foreach (var resourceType in baseResourceTypes)
                {
#if R5
            if (Enum.TryParse<VersionIndependentResourceTypesAll>(resourceType, ignoreCase: true, out var parsedType))
#else
                    if (Enum.TryParse<ResourceType>(resourceType, ignoreCase: true, out var parsedType))
#endif
                    {
                        baseResourceTypeList.Add(parsedType);
                    }
                }
            }

            var searchParam = new SearchParameter
            {
                Url = $"http://example.org/fhir/SearchParameter/{code}",
                Name = code,
                Status = PublicationStatus.Active,
                Code = code,
                Type = searchParamType,
                Expression = expression,
                Description = new Markdown($"Custom search parameter for {string.Join(", ", baseResourceTypes ?? Array.Empty<string>())}"),
                Version = "1.0.0",
                Base = baseResourceTypeList,
            };

            try
            {
                var result = await _fixture.TestFhirClient.CreateAsync(searchParam);
                return result;
            }
            catch (Exception ex)
            {
                // Log the exception for debugging
                System.Diagnostics.Debug.WriteLine($"Failed to create search parameter: {ex.Message}");
                throw;
            }
        }

        private async Task<Observation> CreateObservationResourceAsync(string id, string patientId)
        {
            var observation = new Observation
            {
                Id = id,
                Status = ObservationStatus.Final,
                Code = new CodeableConcept("http://loinc.org", "1234-5"),
                Subject = new ResourceReference($"Patient/{patientId}"),
            };

            try
            {
                var result = await _fixture.TestFhirClient.CreateAsync(observation);
                return result;
            }
            catch
            {
                return observation;
            }
        }

        private async Task<Specimen> CreateSpecimenResourceAsync(string id, string name)
        {
            var specimen = new Specimen
            {
                Id = id,
                Status = Specimen.SpecimenStatus.Available,
                Type = new CodeableConcept("http://snomed.info/sct", "119295008", "Specimen"),
            };

            try
            {
                var result = await _fixture.TestFhirClient.CreateAsync(specimen);
                return result;
            }
            catch
            {
                return specimen;
            }
        }

        private async Task<Immunization> CreateImmunizationResourceAsync(string id, string patientId)
        {
            var immunization = new Immunization
            {
                Id = id,
                Status = Immunization.ImmunizationStatusCodes.Completed,
                VaccineCode = new CodeableConcept("http://hl7.org/fhir/sid/cvx", "207"),
                Patient = new ResourceReference($"Patient/{patientId}"),
            };

            try
            {
                var result = await _fixture.TestFhirClient.CreateAsync(immunization);
                return result;
            }
            catch
            {
                return immunization;
            }
        }

        /// <summary>
        /// Cleanup method that handles both test data resources (Patient, Observation, etc.) and search parameters.
        /// Ensures all created resources are properly deleted and reindex is run to finalize changes.
        /// </summary>
        private async Task CleanupTestDataAsync(List<(string resourceType, string resourceId)> testResources, params SearchParameter[] searchParameters)
        {
            // Delete test data resources (Patient, Observation, etc.)
            if (testResources != null && testResources.Count > 0)
            {
                foreach (var (resourceType, resourceId) in testResources)
                {
                    try
                    {
                        if (!string.IsNullOrEmpty(resourceId))
                        {
                            await _fixture.TestFhirClient.DeleteAsync($"{resourceType}/{resourceId}");
                            System.Diagnostics.Debug.WriteLine($"Deleted {resourceType}/{resourceId}");
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to delete {resourceType}/{resourceId}: {ex.Message}");
                    }
                }
            }

            // Delete search parameters (soft delete with reindex finalization)
            await CleanupSearchParametersAsync(searchParameters);
        }

        private async Task CleanupSearchParametersAsync(params SearchParameter[] searchParameters)
        {
            // Validate input
            if (searchParameters == null || searchParameters.Length == 0)
            {
                return;
            }

            // Delete created search parameters
            foreach (var param in searchParameters)
            {
                try
                {
                    if (param != null && !string.IsNullOrEmpty(param.Id))
                    {
                        // Attempt to delete the search parameter
                        // Note: This performs a soft delete, the parameter may be in PendingDelete status
                        await _fixture.TestFhirClient.DeleteAsync($"SearchParameter/{param.Id}");
                    }
                }
                catch (Exception ex)
                {
                    // Log but continue cleanup even if delete fails
                    System.Diagnostics.Debug.WriteLine($"Failed to delete SearchParameter/{param?.Id}: {ex.Message}");
                }
            }

            // Allow time for soft deletes to be processed
            await Task.Delay(500);

            // Note: Final reindex is handled by ReindexTestFixture.OnDisposedAsync() which runs
            // once at the very end after all tests complete. This eliminates the need to reindex
            // after each individual test cleanup.
        }

        private async Task<OperationStatus> WaitForJobCompletionAsync(Uri jobUri, TimeSpan timeout)
        {
            var stopwatch = Stopwatch.StartNew();
            OperationStatus lastStatus = OperationStatus.Queued;

            while (stopwatch.Elapsed < timeout)
            {
                try
                {
                    var jobResponse = await _fixture.TestFhirClient.CheckJobAsync(jobUri);

                    if (jobResponse.Resource?.Parameter != null)
                    {
                        var statusParam = jobResponse.Resource.Parameter
                            .FirstOrDefault(p => p.Name == "status");

                        if (statusParam?.Value != null)
                        {
                            string statusString = null;

                            // Handle both FhirString and Code value types
                            if (statusParam.Value is FhirString fhirString)
                            {
                                statusString = fhirString.Value;
                            }
                            else if (statusParam.Value is Code code)
                            {
                                statusString = code.Value;
                            }

                            if (!string.IsNullOrEmpty(statusString) &&
                                Enum.TryParse<OperationStatus>(statusString, true, out var status))
                            {
                                lastStatus = status;

                                if (status == OperationStatus.Completed ||
                                    status == OperationStatus.Failed ||
                                    status == OperationStatus.Canceled)
                                {
                                    return status;
                                }
                            }
                        }
                    }
                }
                catch
                {
                    // Continue polling even if there are errors
                }

                await Task.Delay(1000);
            }

            return lastStatus;
        }

        private async Task<System.Net.Http.HttpResponseMessage> CancelReindexJobAsync(Uri jobUri)
        {
            using var request = new System.Net.Http.HttpRequestMessage
            {
                Method = System.Net.Http.HttpMethod.Delete,
                RequestUri = jobUri,
            };

            return await _fixture.TestFhirClient.HttpClient.SendAsync(request);
        }

        /// <summary>
        /// Checks if a search response contains a "NotSupported" error in an OperationOutcome.
        /// This indicates that a search parameter is not supported (likely because it was deleted or failed to index).
        /// </summary>
        /// <param name="searchResponse">The search response bundle to check</param>
        /// <returns>True if a "NotSupported" error is found, false otherwise</returns>
        private static bool HasNotSupportedError(Bundle searchResponse)
        {
            return searchResponse?.Entry?.Any(e =>
                e.Resource is OperationOutcome oo &&
                oo.Issue?.Any(i => i.Code?.ToString() == "NotSupported") == true) ?? false;
        }

        /// <summary>
        /// Verifies that a search parameter is properly indexed and working by executing a search query.
        /// If the search parameter is not indexed, the response will contain an OperationOutcome with code "NotSupported".
        /// This method ensures the search parameter is fully functional after reindex operations and validates
        /// that the expected records are returned (or not found, depending on the scenario).
        /// </summary>
        /// <param name="searchQuery">The search query to execute (e.g., "Patient?code=value")</param>
        /// <param name="searchParameterCode">The search parameter code for error messaging</param>
        /// <param name="expectedResourceType">The resource type being searched for (e.g., "Patient")</param>
        /// <param name="shouldFindRecords">Whether we expect to find records. True if data should be returned, false if expecting empty result</param>
        private async Task VerifySearchParameterIsWorkingAsync(
            string searchQuery,
            string searchParameterCode,
            string expectedResourceType = null,
            bool shouldFindRecords = true)
        {
            try
            {
                var searchResponse = await _fixture.TestFhirClient.SearchAsync(searchQuery);
                Assert.NotNull(searchResponse);

                // Verify the search parameter is supported (no "NotSupported" in the response)
                var hasNotSupportedError = HasNotSupportedError(searchResponse.Resource);

                Assert.False(
                    hasNotSupportedError,
                    $"Search parameter {searchParameterCode} should be supported after reindex. Got 'NotSupported' error in response.");
                System.Diagnostics.Debug.WriteLine(
                    $"Search parameter {searchParameterCode} is working - search executed successfully without 'NotSupported' error");

                // Validate record expectations if specified
                if (!string.IsNullOrEmpty(expectedResourceType))
                {
                    var resultEntries = searchResponse.Resource?.Entry ?? new List<Bundle.EntryComponent>();
                    var resourcesFound = resultEntries
                        .Where(e => e.Resource?.TypeName == expectedResourceType)
                        .ToList();

                    if (shouldFindRecords)
                    {
                        Assert.NotEmpty(resourcesFound);
                        Assert.True(
                            resourcesFound.Count > 0,
                            $"Expected to find {expectedResourceType} records for search parameter {searchParameterCode}, but none were returned.");
                        System.Diagnostics.Debug.WriteLine(
                            $"Search parameter {searchParameterCode} correctly returned {resourcesFound.Count} {expectedResourceType} record(s)");
                    }
                    else
                    {
                        Assert.Empty(resourcesFound);
                        Assert.True(
                            resourcesFound.Count == 0,
                            $"Expected no {expectedResourceType} records for search parameter {searchParameterCode}, but found {resourcesFound.Count}.");
                        System.Diagnostics.Debug.WriteLine(
                            $"Search parameter {searchParameterCode} correctly returned no {expectedResourceType} records");
                    }
                }

                // Log the successful search query for reference
                System.Diagnostics.Debug.WriteLine($"Search query: {searchQuery} executed successfully.");
            }
            catch (Exception ex)
            {
                throw new Xunit.Sdk.XunitException(
                    $"Search parameter {searchParameterCode} should be usable after reindex. Error: {ex.Message}",
                    ex);
            }
        }

        private static string ExtractJobIdFromUri(Uri uri)
        {
            // URI format: .../_operations/reindex/{jobId}
            var parts = uri.AbsolutePath.Split('/');
            return parts.Length > 0 ? parts[^1] : string.Empty;
        }
    }
}
