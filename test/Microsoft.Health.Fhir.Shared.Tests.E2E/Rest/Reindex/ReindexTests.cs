// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Microsoft.Health.Extensions.Xunit;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Test.Utilities;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Reindex
{
    [CollectionDefinition(Categories.IndexAndReindex, DisableParallelization = true)]
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

        ////[Fact]
        ////public async Task GivenReindexJobWithConcurrentUpdates_ThenReportedCountsAreLessThanOriginal()
        ////{
        ////    await CancelAnyRunningReindexJobsAsync();

        ////    var searchParam = new SearchParameter();
        ////    var testResources = new List<(string resourceType, string resourceId)>();
        ////    (FhirResponse<Parameters> response, Uri jobUri) value = default;

        ////    try
        ////    {
        ////        var randomSuffix = Guid.NewGuid().ToString("N").Substring(0, 8);
        ////        var resources = (await SetupTestDataAsync("Person", 20, randomSuffix, CreatePersonResourceAsync)).createdResources;
        ////        testResources.AddRange(resources);
        ////        searchParam = await CreateCustomSearchParameterAsync($"custom-person-name-{randomSuffix}", ["Person"], "Person.name.given", SearchParamType.String);
        ////        Assert.NotNull(searchParam);

        ////        var parameters = new Parameters
        ////        {
        ////            Parameter =
        ////            [
        ////                new Parameters.ParameterComponent { Name = "maximumNumberOfResourcesPerQuery", Value = new Integer(1) },
        ////                new Parameters.ParameterComponent { Name = "maximumNumberOfResourcesPerWrite", Value = new Integer(1) },
        ////            ],
        ////        };

        ////        value = await _fixture.TestFhirClient.PostReindexJobAsync(parameters);
        ////        Assert.Equal(HttpStatusCode.Created, value.response.Response.StatusCode);

        ////        var tasks = new[]
        ////        {
        ////            WaitForJobCompletionAsync(value.jobUri, TimeSpan.FromSeconds(300)),
        ////            RandomPersonUpdate(testResources),
        ////        };
        ////        await Task.WhenAll(tasks);

        ////        // reported in reindex counts should be less than total resources created
        ////        await CheckCounts(value.jobUri, testResources.Count, testResources.Count, true);
        ////    }
        ////    finally
        ////    {
        ////        await CleanupTestDataAsync(testResources, searchParam);
        ////    }
        ////}

        [Fact]
        public async Task GivenReindexJobWithMixedZeroAndNonZeroCountResources_WhenReindexCompletes_ThenSearchParametersShouldWork()
        {
            var storageMultiplier = _fixture.DataStore == DataStore.CosmosDb ? 50 : 1; // allows to keep settings for cosmos and optimize sql

            // Cancel any running reindex jobs before starting this test
            await CancelAnyRunningReindexJobsAsync();

            // Scenario 1: Test that search parameter status is only updated when ALL jobs complete successfully
            // This tests both zero-count and non-zero-count resource scenarios with multiple resource types
            var mixedBaseSearchParam = new SearchParameter();
            var personSearchParam = new SearchParameter();
            var randomSuffix = Guid.NewGuid().ToString("N").Substring(0, 8);
            var testResources = new List<(string resourceType, string resourceId)>();
            var supplyDeliveryCount = 40 * storageMultiplier;
            var personCount = 20 * storageMultiplier;
            (FhirResponse<Parameters> response, Uri jobUri) value = default;

            try
            {
                // Set up test data using the common setup method
                Debug.WriteLine($"Setting up test data for SupplyDelivery and Person resources...");

                // Setup Persons first, then SupplyDeliveries sequentially (not in parallel)
                // This ensures Persons are fully created before SupplyDeliveries start
                var (personResources, finalPersonCount) = await SetupTestDataAsync("Person", personCount, randomSuffix, CreatePersonResourceAsync);
                testResources.AddRange(personResources);

                // CRITICAL: Verify we got what we expected
                Assert.True(
                    finalPersonCount >= personCount,
                    $"Failed to create sufficient Person resources. Expected: {personCount}, Got: {finalPersonCount}");

                var (supplyDeliveryResources, finalSupplyDeliveryCount) = await SetupTestDataAsync("SupplyDelivery", supplyDeliveryCount, randomSuffix, CreateSupplyDeliveryResourceAsync);
                testResources.AddRange(supplyDeliveryResources);

                // CRITICAL: Verify we got what we expected
                Assert.True(
                    finalSupplyDeliveryCount >= supplyDeliveryCount,
                    $"Failed to create sufficient SupplyDelivery resources. Expected: {supplyDeliveryCount}, Got: {finalSupplyDeliveryCount}");

                Debug.WriteLine($"Test data setup complete - SupplyDelivery: {finalSupplyDeliveryCount}, Person: {finalPersonCount}");

                // Create a single search parameter that applies to BOTH SupplyDelivery and Immunization
                // This allows us to test the scenario where one resource type has data and another has none
                mixedBaseSearchParam = await CreateCustomSearchParameterAsync(
                    $"custom-mixed-base-{randomSuffix}",
                    ["SupplyDelivery", "Immunization"],  // Applies to both resource types
                    "SupplyDelivery.status",  // Valid for SupplyDelivery
                    SearchParamType.Token);
                Assert.NotNull(mixedBaseSearchParam);

                // Create a separate search parameter specifically for Person resources
                // This will create a separate reindex job for Person resources
                personSearchParam = await CreateCustomSearchParameterAsync(
                    $"custom-person-name-{randomSuffix}",
                    ["Person"],  // Applies only to Person
                    "Person.name.given",  // Valid for Person name
                    SearchParamType.String);
                Assert.NotNull(personSearchParam);

                // Create reindex job targeting both search parameters
                // This will trigger multiple reindex jobs (one for SupplyDelivery/Immunization, one for Person)
                var parameters = new Parameters
                {
                    Parameter = new List<Parameters.ParameterComponent>
                    {
                        // do not disturb cosmos as it migh affect its pagination
                        new Parameters.ParameterComponent { Name = "maximumNumberOfResourcesPerQuery", Value = new Integer(10 * storageMultiplier) },
                        new Parameters.ParameterComponent { Name = "maximumNumberOfResourcesPerWrite", Value = new Integer(10 * storageMultiplier) },
                    },
                };

                value = await _fixture.TestFhirClient.PostReindexJobAsync(parameters);

                Assert.Equal(HttpStatusCode.Created, value.response.Response.StatusCode);
                Assert.NotNull(value.jobUri);

                // Wait for job to complete (this will wait for all sub-jobs to complete)
                var jobStatus = await WaitForJobCompletionAsync(value.jobUri, TimeSpan.FromSeconds(300));
                Assert.True(
                    jobStatus == OperationStatus.Completed,
                    $"Expected Completed, got {jobStatus}");

                await CheckCounts(value.jobUri, testResources.Count, testResources.Count, false);

                // Verify search parameter is working for SupplyDelivery (which has data)
                // Use the ACTUAL count we got, not the desired count
                await VerifySearchParameterIsWorkingAsync(
                    $"SupplyDelivery?{mixedBaseSearchParam.Code}=in-progress",
                    mixedBaseSearchParam.Code,
                    expectedResourceType: "SupplyDelivery",
                    shouldFindRecords: true);

                // Verify search parameter is working for Immunization (which has no data)
                // We expect no immunization records to be returned (empty result set)
                await VerifySearchParameterIsWorkingAsync(
                    $"Immunization?{mixedBaseSearchParam.Code}=207",
                    mixedBaseSearchParam.Code,
                    expectedResourceType: "Immunization",
                    shouldFindRecords: false);

                // Verify search parameter is working for Person (which has data)
                // Use the ACTUAL count we got, not the desired count
                await VerifySearchParameterIsWorkingAsync(
                    $"Person?{personSearchParam.Code}=Test",
                    personSearchParam.Code,
                    expectedResourceType: "Person",
                    shouldFindRecords: true);
            }
            finally
            {
                // Cleanup all test data including resources and search parameters
                Debug.WriteLine($"Starting cleanup of {testResources.Count} test resources...");
                await CleanupTestDataAsync(testResources, mixedBaseSearchParam, personSearchParam);
                Debug.WriteLine("Cleanup completed");
            }
        }

        ////[Fact]
        ////public async Task GivenReindexJobWithResourceAndAddedAfterSingleCustomSearchParameterAndBeforeReindex_WhenReindexCompletes_ThenSearchParameterShouldWork()
        ////{
        ////    // Cancel any running reindex jobs before starting this test
        ////    await CancelAnyRunningReindexJobsAsync();

        ////    // Scenario 2: Test that search parameter with invalid expression fails indexing
        ////    // This validates that indexing failures prevent status updates
        ////    var randomSuffix = Guid.NewGuid().ToString("N").Substring(0, 8);
        ////    var specimenSearchParam = new SearchParameter();
        ////    var immunizationSearchParam = new SearchParameter();
        ////    var testResources = new List<(string resourceType, string resourceId)>();
        ////    (FhirResponse<Parameters> response, Uri jobUri) value = default;

        ////    try
        ////    {
        ////        // Create custom search parameters - one with valid FHIRPath expression
        ////        specimenSearchParam = await CreateCustomSearchParameterAsync($"custom-parameter-before-specimen-{randomSuffix}", ["Specimen"], "Specimen.type", SearchParamType.Token);

        ////        // Create test resources that will be indexed using SetupTestDataAsync
        ////        var (specimenResources, finalSpecimenCount) = await SetupTestDataAsync("Specimen", 1, randomSuffix, CreateSpecimenResourceAsync);
        ////        testResources.AddRange(specimenResources);

        ////        // Create reindex job targeting both search parameters
        ////        var parameters = new Parameters
        ////        {
        ////            Parameter = new List<Parameters.ParameterComponent>
        ////            {
        ////                new Parameters.ParameterComponent
        ////                {
        ////                    Name = "maximumNumberOfResourcesPerQuery",
        ////                    Value = new Integer(10000),
        ////                },
        ////                new Parameters.ParameterComponent
        ////                {
        ////                    Name = "maximumNumberOfResourcesPerWrite",
        ////                    Value = new Integer(1000),
        ////                },
        ////            },
        ////        };

        ////        value = await _fixture.TestFhirClient.PostReindexJobAsync(parameters);

        ////        Assert.Equal(HttpStatusCode.Created, value.response.Response.StatusCode);
        ////        Assert.NotNull(value.jobUri);

        ////        // Wait for job to complete
        ////        var jobStatus = await WaitForJobCompletionAsync(value.jobUri, TimeSpan.FromSeconds(300));
        ////        Assert.True(
        ////            jobStatus == OperationStatus.Completed,
        ////            $"Expected Completed, got {jobStatus}");

        ////        // The valid search parameter should still be usable
        ////        await VerifySearchParameterIsWorkingAsync(
        ////            $"Specimen?{specimenSearchParam.Code}=119295008",
        ////            specimenSearchParam.Code,
        ////            "Specimen",
        ////            true);
        ////    }
        ////    finally
        ////    {
        ////        // Cleanup all test data including resources and search parameters
        ////        await CleanupTestDataAsync(testResources, specimenSearchParam, immunizationSearchParam);
        ////    }
        ////}

        ////[Fact]
        ////public async Task GivenReindexJobWithResourceAndAddedAfterMultiCustomSearchParameterAndBeforeReindex_WhenReindexCompletes_ThenSearchParametersShouldWork()
        ////{
        ////    // Cancel any running reindex jobs before starting this test
        ////    await CancelAnyRunningReindexJobsAsync();

        ////    // Scenario 2: Test that search parameter with invalid expression fails indexing
        ////    // This validates that indexing failures prevent status updates
        ////    var randomSuffix = Guid.NewGuid().ToString("N").Substring(0, 8);
        ////    var specimenSearchParam = new SearchParameter();
        ////    var immunizationSearchParam = new SearchParameter();
        ////    var testResources = new List<(string resourceType, string resourceId)>();
        ////    (FhirResponse<Parameters> response, Uri jobUri) value = default;

        ////    try
        ////    {
        ////        // Create custom search parameters - one with valid FHIRPath expression
        ////        specimenSearchParam = await CreateCustomSearchParameterAsync($"custom-parameter-before-specimen-{randomSuffix}", ["Specimen"], "Specimen.type", SearchParamType.Token);
        ////        immunizationSearchParam = await CreateCustomSearchParameterAsync($"custom-parameter-before-imm-{randomSuffix}", ["Immunization"], "Immunization.vaccineCode", SearchParamType.Token);

        ////        // Create test resources that will be indexed using SetupTestDataAsync
        ////        var (specimenResources, finalSpecimenCount) = await SetupTestDataAsync("Specimen", 1, randomSuffix, CreateSpecimenResourceAsync);
        ////        testResources.AddRange(specimenResources);

        ////        // Create reindex job targeting both search parameters
        ////        var parameters = new Parameters
        ////        {
        ////            Parameter = new List<Parameters.ParameterComponent>
        ////            {
        ////                new Parameters.ParameterComponent
        ////                {
        ////                    Name = "maximumNumberOfResourcesPerQuery",
        ////                    Value = new Integer(10000),
        ////                },
        ////                new Parameters.ParameterComponent
        ////                {
        ////                    Name = "maximumNumberOfResourcesPerWrite",
        ////                    Value = new Integer(1000),
        ////                },
        ////            },
        ////        };

        ////        value = await _fixture.TestFhirClient.PostReindexJobAsync(parameters);

        ////        Assert.Equal(HttpStatusCode.Created, value.response.Response.StatusCode);
        ////        Assert.NotNull(value.jobUri);

        ////        // Wait for job to complete
        ////        var jobStatus = await WaitForJobCompletionAsync(value.jobUri, TimeSpan.FromSeconds(300));
        ////        Assert.True(
        ////            jobStatus == OperationStatus.Completed,
        ////            $"Expected Completed, got {jobStatus}");

        ////        // The valid search parameter should still be usable
        ////        await VerifySearchParameterIsWorkingAsync(
        ////            $"Specimen?{specimenSearchParam.Code}=119295008",
        ////            specimenSearchParam.Code,
        ////            "Specimen",
        ////            true);
        ////    }
        ////    finally
        ////    {
        ////        // Cleanup all test data including resources and search parameters
        ////        await CleanupTestDataAsync(testResources, specimenSearchParam, immunizationSearchParam);
        ////    }
        ////}

        ////[Fact]
        ////public async Task GivenReindexWithCaseVariantSearchParameterUrls_WhenBothHaveSameStatus_ThenBothShouldBeProcessedCorrectly()
        ////{
        ////    // Cancel any running reindex jobs before starting this test
        ////    await CancelAnyRunningReindexJobsAsync();

        ////    // Scenario 3a: Case variant search parameter URLs with same status (Supported, Supported)
        ////    // Both should be treated as separate entries and processed correctly
        ////    var randomSuffix = Guid.NewGuid().ToString("N").Substring(0, 8);
        ////    var lowerCaseParam = new SearchParameter();
        ////    var upperCaseParam = new SearchParameter();
        ////    var testResources = new List<(string resourceType, string resourceId)>();
        ////    (FhirResponse<Parameters> response, Uri jobUri) value = default;

        ////    try
        ////    {
        ////        // Create test resource using SetupTestDataAsync
        ////        var (specimenResources, finalSpecimenCount) = await SetupTestDataAsync("Specimen", 1, randomSuffix, CreateSpecimenResourceAsync);
        ////        testResources.AddRange(specimenResources);

        ////        // Create search parameters with unique codes
        ////        lowerCaseParam = await CreateCustomSearchParameterAsync($"custom-case-sensitive-{randomSuffix}", ["Specimen"], "Specimen.type", SearchParamType.Token);
        ////        upperCaseParam = await CreateCustomSearchParameterAsync($"custom-case-Sensitive-{randomSuffix}", ["Specimen"], "Specimen.status", SearchParamType.Token);
        ////        Assert.NotNull(lowerCaseParam);
        ////        Assert.NotNull(upperCaseParam);

        ////        // Create reindex job targeting both case-variant search parameters
        ////        var parameters = new Parameters
        ////        {
        ////            Parameter = new List<Parameters.ParameterComponent>
        ////            {
        ////                new Parameters.ParameterComponent
        ////                {
        ////                    Name = "maximumNumberOfResourcesPerQuery",
        ////                    Value = new Integer(10000),
        ////                },
        ////                new Parameters.ParameterComponent
        ////                {
        ////                    Name = "maximumNumberOfResourcesPerWrite",
        ////                    Value = new Integer(1000),
        ////                },
        ////            },
        ////        };

        ////        value = await _fixture.TestFhirClient.PostReindexJobAsync(parameters);

        ////        Assert.Equal(HttpStatusCode.Created, value.response.Response.StatusCode);
        ////        Assert.NotNull(value.jobUri);

        ////        // Wait for job completion
        ////        var jobStatus = await WaitForJobCompletionAsync(value.jobUri, TimeSpan.FromSeconds(300));
        ////        Assert.True(
        ////            jobStatus == OperationStatus.Completed,
        ////            $"Expected Completed, got {jobStatus}");

        ////        // Verify both search parameters are working after reindex
        ////        await VerifySearchParameterIsWorkingAsync(
        ////            $"Specimen?{lowerCaseParam.Code}=119295008",
        ////            lowerCaseParam.Code);
        ////        await VerifySearchParameterIsWorkingAsync(
        ////            $"Specimen?{upperCaseParam.Code}=available",
        ////            upperCaseParam.Code);
        ////    }
        ////    finally
        ////    {
        ////        // Cleanup all test data including resources and search_parameters
        ////        await CleanupTestDataAsync(testResources, lowerCaseParam, upperCaseParam);
        ////    }
        ////}

        ////[Fact]
        ////public async Task GivenReindexWithCaseVariantSearchParameterUrls_WhenHavingDifferentStatuses_ThenBothSearchParametersShouldWork()
        ////{
        ////    // Cancel any running reindex jobs before starting this test
        ////    await CancelAnyRunningReindexJobsAsync();

        ////    // Scenario 3b: Case variant search parameter URLs with different statuses
        ////    // Verify both are set to the correct status when all jobs complete
        ////    var randomSuffix = Guid.NewGuid().ToString("N").Substring(0, 8);
        ////    var specimenTypeParam = new SearchParameter();
        ////    var specimenStatusParam = new SearchParameter();
        ////    var testResources = new List<(string resourceType, string resourceId)>();
        ////    (FhirResponse<Parameters> response, Uri jobUri) value = default;

        ////    try
        ////    {
        ////        // Create test resource using SetupTestDataAsync
        ////        var (specimenResources, finalSpecimenCount) = await SetupTestDataAsync("Specimen", 1, randomSuffix, CreateSpecimenResourceAsync);
        ////        testResources.AddRange(specimenResources);

        ////        // Create custom search parameters with different expressions and unique codes
        ////        specimenTypeParam = await CreateCustomSearchParameterAsync($"custom-diff-type-{randomSuffix}", ["Specimen"], "Specimen.type", SearchParamType.Token);
        ////        specimenStatusParam = await CreateCustomSearchParameterAsync($"custom-diff-status-{randomSuffix}", ["Specimen"], "Specimen.status", SearchParamType.Token);
        ////        Assert.NotNull(specimenTypeParam);
        ////        Assert.NotNull(specimenStatusParam);

        ////        // Create reindex job
        ////        var parameters = new Parameters
        ////        {
        ////            Parameter = new List<Parameters.ParameterComponent>
        ////            {
        ////                new Parameters.ParameterComponent
        ////                {
        ////                    Name = "maximumNumberOfResourcesPerQuery",
        ////                    Value = new Integer(10000),
        ////                },
        ////                new Parameters.ParameterComponent
        ////                {
        ////                    Name = "maximumNumberOfResourcesPerWrite",
        ////                    Value = new Integer(1000),
        ////                },
        ////            },
        ////        };

        ////        value = await _fixture.TestFhirClient.PostReindexJobAsync(parameters);

        ////        Assert.Equal(HttpStatusCode.Created, value.response.Response.StatusCode);
        ////        Assert.NotNull(value.jobUri);

        ////        // Wait for job completion
        ////        var jobStatus = await WaitForJobCompletionAsync(value.jobUri, TimeSpan.FromSeconds(300));
        ////        Assert.True(
        ////            jobStatus == OperationStatus.Completed,
        ////            $"Expected Completed, got {jobStatus}");

        ////        // Verify both search parameters are working after reindex
        ////        await VerifySearchParameterIsWorkingAsync(
        ////            $"Specimen?{specimenTypeParam.Code}=119295008",
        ////            specimenTypeParam.Code);
        ////        await VerifySearchParameterIsWorkingAsync(
        ////            $"Specimen?{specimenStatusParam.Code}=available",
        ////            specimenStatusParam.Code);
        ////    }
        ////    finally
        ////    {
        ////        // Cleanup all test data including resources and search parameters
        ////        await CleanupTestDataAsync(testResources, specimenTypeParam, specimenStatusParam);
        ////    }
        ////}

        ////[Fact]
        ////public async Task GivenSearchParameterAddedAndReindexed_WhenSearchParameterIsDeleted_ThenAfterReindexSearchParameterShouldNotBeSupported()
        ////{
        ////    // Cancel any running reindex jobs before starting this test
        ////    await CancelAnyRunningReindexJobsAsync();

        ////    // Comprehensive lifecycle test:
        ////    // 1. Create a Specimen record with specific data
        ////    // 2. Add a custom search parameter
        ////    // 3. Reindex to index the new search parameter
        ////    // 4. Verify the search parameter works and returns the Specimen
        ////    // 5. Delete the search parameter
        ////    // 6. Reindex to remove the search parameter from the index
        ////    // 7. Verify the search parameter is no longer supported (returns not-supported error)
        ////    var randomSuffix = Guid.NewGuid().ToString("N").Substring(0, 8);
        ////    var searchParam = new SearchParameter();
        ////    var specimenId = string.Empty;
        ////    var testResources = new List<(string resourceType, string resourceId)>();
        ////    (FhirResponse<Parameters> response, Uri jobUri) reindexRequest1 = default;
        ////    (FhirResponse<Parameters> response, Uri jobUri) reindexRequest2 = default;

        ////    try
        ////    {
        ////        // Step 1: Create a Specimen with specific data that matches our search parameter expression
        ////        var specimenType = $"LifecycleTest{randomSuffix}";
        ////        var (specimenResources, finalSpecimenCount) = await SetupTestDataAsync("Specimen", 1, randomSuffix, CreateSpecimenResourceAsync);
        ////        testResources.AddRange(specimenResources);

        ////        if (specimenResources.Count > 0)
        ////        {
        ////            specimenId = specimenResources[0].resourceId;
        ////            System.Diagnostics.Debug.WriteLine($"Created specimen with ID {specimenId} and type {specimenType}");
        ////        }

        ////        // Step 2: Create a custom search parameter for Specimen.type
        ////        searchParam = await CreateCustomSearchParameterAsync(
        ////            $"custom-lifecycle-{randomSuffix}",
        ////            ["Specimen"],
        ////            "Specimen.type",
        ////            SearchParamType.Token);
        ////        Assert.NotNull(searchParam);
        ////        System.Diagnostics.Debug.WriteLine($"Created search parameter with code {searchParam.Code} and ID {searchParam.Id}");

        ////        // Step 3: Reindex to index the newly created search parameter
        ////        var reindexParams = new Parameters
        ////        {
        ////            Parameter = new List<Parameters.ParameterComponent>
        ////            {
        ////                new Parameters.ParameterComponent
        ////                {
        ////                    Name = "maximumNumberOfResourcesPerQuery",
        ////                    Value = new Integer(10000),
        ////                },
        ////                new Parameters.ParameterComponent
        ////                {
        ////                    Name = "maximumNumberOfResourcesPerWrite",
        ////                    Value = new Integer(1000),
        ////                },
        ////            },
        ////        };

        ////        reindexRequest1 = await _fixture.TestFhirClient.PostReindexJobAsync(reindexParams);
        ////        Assert.Equal(HttpStatusCode.Created, reindexRequest1.response.Response.StatusCode);
        ////        Assert.NotNull(reindexRequest1.jobUri);
        ////        System.Diagnostics.Debug.WriteLine("Started first reindex job to index the new search parameter");

        ////        var jobStatus1 = await WaitForJobCompletionAsync(reindexRequest1.jobUri, TimeSpan.FromSeconds(240));
        ////        Assert.True(
        ////            jobStatus1 == OperationStatus.Completed,
        ////            $"First reindex job should complete successfully, but got {jobStatus1}");
        ////        System.Diagnostics.Debug.WriteLine("First reindex job completed successfully");

        ////        // Step 4: Verify the search parameter works by searching for the specimen
        ////        var searchQuery = $"Specimen?{searchParam.Code}=119295008";
        ////        await VerifySearchParameterIsWorkingAsync(
        ////            searchQuery,
        ////            searchParam.Code,
        ////            expectedResourceType: "Specimen",
        ////            shouldFindRecords: true);

        ////        // Step 5: Delete the search parameter
        ////        await _fixture.TestFhirClient.DeleteAsync($"SearchParameter/{searchParam.Id}");
        ////        System.Diagnostics.Debug.WriteLine($"Deleted search parameter {searchParam.Code} (ID: {searchParam.Id})");

        ////        // Step 6: Reindex to remove the search parameter from the index
        ////        reindexRequest2 = await _fixture.TestFhirClient.PostReindexJobAsync(reindexParams);
        ////        Assert.Equal(HttpStatusCode.Created, reindexRequest2.response.Response.StatusCode);
        ////        Assert.NotNull(reindexRequest2.jobUri);
        ////        System.Diagnostics.Debug.WriteLine("Started second reindex job to remove the deleted search parameter");

        ////        var jobStatus2 = await WaitForJobCompletionAsync(reindexRequest2.jobUri, TimeSpan.FromSeconds(240));
        ////        Assert.True(
        ////            jobStatus2 == OperationStatus.Completed,
        ////            $"Second reindex job should complete successfully, but got {jobStatus2}");
        ////        System.Diagnostics.Debug.WriteLine("Second reindex job completed successfully");

        ////        // Step 7: Verify the search parameter is no longer supported
        ////        var postDeleteSearchResponse = await _fixture.TestFhirClient.SearchAsync(searchQuery);
        ////        Assert.NotNull(postDeleteSearchResponse);
        ////        System.Diagnostics.Debug.WriteLine($"Executed search query after deletion: {searchQuery}");

        ////        // Verify that a "NotSupported" error is now present
        ////        var hasNotSupportedErrorAfterDelete = HasNotSupportedError(postDeleteSearchResponse.Resource);

        ////        Assert.True(
        ////            hasNotSupportedErrorAfterDelete,
        ////            $"Search parameter {searchParam.Code} should NOT be supported after deletion and reindex. Got 'NotSupported' error in response.");
        ////        System.Diagnostics.Debug.WriteLine($"Search parameter {searchParam.Code} correctly returns 'NotSupported' error after deletion");
        ////    }
        ////    finally
        ////    {
        ////        // Cleanup any remaining resources
        ////        await CleanupTestDataAsync(testResources, searchParam);
        ////    }
        ////}

        // left as async to minimize changes
        private async Task<Person> CreatePersonResourceAsync(string id, string name)
        {
            return await Task.FromResult(CreatePersonResource(id, name));
        }

        private Person CreatePersonResource(string id, string name)
        {
            return new Person { Id = id, Name = [new() { Given = [name] }] };
        }

        /// <summary>
        /// Helper method to create and post a resource in a single operation
        /// Returns a tuple indicating success and the resource/ID
        /// </summary>
        private async Task<(bool success, T resource, string id)> CreateAndPostResourceWithStatusAsync<T>(string id, string name, Func<string, string, Task<T>> createResourceFunc)
            where T : Resource
        {
            try
            {
                var resource = await createResourceFunc(id, name);

                // Post the resource using the client's CreateAsync method
                var response = await _fixture.TestFhirClient.CreateAsync(resource);

                if (response?.Resource != null && !string.IsNullOrEmpty(response.Resource.Id))
                {
                    System.Diagnostics.Debug.WriteLine($"Successfully created {typeof(T).Name}/{response.Resource.Id}");
                    return (true, response.Resource, response.Resource.Id);
                }

                System.Diagnostics.Debug.WriteLine($"Failed to create resource {id}: Response was null or had no ID");
                return (false, resource, id);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to create resource {id}: {ex.Message}");
                return (false, null, id);
            }
        }

        /// <summary>
        /// Helper method to create and post a resource in a single operation
        /// </summary>
        private async Task<T> CreateAndPostResourceAsync<T>(string id, string name, Func<string, string, Task<T>> createResourceFunc)
            where T : Resource
        {
            var resource = await createResourceFunc(id, name);

            try
            {
                // Post the resource using the client's CreateAsync method
                var response = await _fixture.TestFhirClient.CreateAsync(resource);
                return response;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to create resource {id}: {ex.Message}");
                return resource; // Return the original resource even on failure so ID can be tracked
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

        private async Task<Observation> CreateObservationResourceAsync(string id, string personId)
        {
            var observation = new Observation
            {
                Id = id,
                Status = ObservationStatus.Final,
                Code = new CodeableConcept("http://loinc.org", "1234-5"),
                Subject = new ResourceReference($"Person/{personId}"),
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
                Subject = new ResourceReference($"Patient/{id}"),
            };

            // Return the specimen object without posting - bundle transaction will handle the post
            return await Task.FromResult(specimen);
        }

        private async Task<SupplyDelivery> CreateSupplyDeliveryResourceAsync(string id, string patientId)
        {
            var supplyDelivery = new SupplyDelivery
            {
                Id = id,
                Status = SupplyDelivery.SupplyDeliveryStatus.InProgress,
                Patient = new ResourceReference($"Patient/{patientId}"),
            };

            // Return the supply delivery object without posting - will be posted in parallel batches
            return await Task.FromResult(supplyDelivery);
        }

        /// <summary>
        /// Cleanup method that handles both test data resources (Person, Observation, etc.) and search parameters.
        /// Ensures all created resources are properly deleted using parallel batch processing for improved performance.
        /// </summary>
        private async Task CleanupTestDataAsync(List<(string resourceType, string resourceId)> testResources, params SearchParameter[] searchParameters)
        {
            // Delete test data resources (Person, Observation, etc.) in parallel batches
            if (testResources != null && testResources.Count > 0)
            {
                System.Diagnostics.Debug.WriteLine($"Starting cleanup of {testResources.Count} test resources...");

                const int batchSize = 500; // Process 500 resources at a time in parallel
                int totalDeleted = 0;
                int totalFailed = 0;

                for (int batchStart = 0; batchStart < testResources.Count; batchStart += batchSize)
                {
                    int currentBatchSize = Math.Min(batchSize, testResources.Count - batchStart);
                    var batchTasks = new List<Task>();

                    // Create deletion tasks for this batch
                    for (int i = 0; i < currentBatchSize; i++)
                    {
                        var (resourceType, resourceId) = testResources[batchStart + i];

                        if (!string.IsNullOrEmpty(resourceId))
                        {
                            batchTasks.Add(DeleteResourceAsync(resourceType, resourceId));
                        }
                    }

                    try
                    {
                        // Execute all deletes in parallel
                        await Task.WhenAll(batchTasks);
                        totalDeleted += currentBatchSize;

                        System.Diagnostics.Debug.WriteLine($"Deleted batch {(batchStart / batchSize) + 1}: {currentBatchSize} resources (total: {totalDeleted}/{testResources.Count})");
                    }
                    catch (Exception ex)
                    {
                        totalFailed += currentBatchSize;
                        System.Diagnostics.Debug.WriteLine($"Failed to delete batch at offset {batchStart}: {ex.Message}");

                        // Continue with next batch instead of failing completely
                    }
                }

                System.Diagnostics.Debug.WriteLine($"Cleanup completed: {totalDeleted} deleted successfully, {totalFailed} failed");
            }

            await Task.Delay(TimeSpan.FromSeconds(3));

            // Delete search parameters (soft delete with reindex finalization)
            await CleanupSearchParametersAsync(searchParameters);
        }

        /// <summary>
        /// Helper method to delete a single resource with error handling
        /// </summary>
        private async Task DeleteResourceAsync(string resourceType, string resourceId)
        {
            try
            {
                await _fixture.TestFhirClient.DeleteAsync($"{resourceType}/{resourceId}");
                System.Diagnostics.Debug.WriteLine($"Deleted {resourceType}/{resourceId}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to delete {resourceType}/{resourceId}: {ex.Message}");

                // Don't throw - allow other deletions to continue
            }
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
        /// Verifies that a search parameter is properly indexed and working by executing a search query with retry logic.
        /// Retries handle timing issues with search parameter cache refresh after reindex operations.
        /// If the search parameter is not indexed, the response will contain an OperationOutcome with code "NotSupported".
        /// This method ensures the search parameter is fully functional after reindex operations and validates
        /// that results are returned (or not found, depending on the scenario).
        ///
        /// Handles pagination by following NextLink entries until all results are retrieved.
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
            Exception lastException = null;

            var maxRetries = 1;
            var retryDelayMs = 20000;
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    // Use a reasonable default page size
                    const int pageSize = 100;

                    // Add _count parameter to control pagination
                    var queryWithCount = searchQuery.Contains("?")
                        ? $"{searchQuery}&_count={pageSize}"
                        : $"{searchQuery}?_count={pageSize}";

                    var allResourcesFound = new List<Bundle.EntryComponent>();
                    string nextLink = queryWithCount;
                    int pageCount = 0;
                    int totalEntriesRetrieved = 0;

                    System.Diagnostics.Debug.WriteLine(
                        $"Search parameter {searchParameterCode} - attempt {attempt} of {maxRetries}, starting search with page size {pageSize}.");

                    // Follow pagination until we get all results
                    while (!string.IsNullOrEmpty(nextLink))
                    {
                        pageCount++;
                        System.Diagnostics.Debug.WriteLine($"Search parameter {searchParameterCode} - fetching page {pageCount}");

                        var searchResponse = await _fixture.TestFhirClient.SearchAsync(nextLink);
                        Assert.NotNull(searchResponse);

                        // Verify the search parameter is supported (no "NotSupported" in the response)
                        var hasNotSupportedError = HasNotSupportedError(searchResponse.Resource);

                        Assert.False(
                            hasNotSupportedError,
                            $"Search parameter {searchParameterCode} should be supported after reindex for {expectedResourceType}. Got 'NotSupported' error in response.");

                        // Collect all entries from this page
                        if (searchResponse.Resource?.Entry != null)
                        {
                            allResourcesFound.AddRange(searchResponse.Resource.Entry);
                            totalEntriesRetrieved += searchResponse.Resource.Entry.Count;
                        }

                        // Check if there's a next link for pagination
                        nextLink = searchResponse.Resource?.NextLink?.OriginalString;
                        System.Diagnostics.Debug.WriteLine(
                            $"Search parameter {searchParameterCode} - page {pageCount} returned {searchResponse.Resource?.Entry?.Count ?? 0} entries (total so far: {totalEntriesRetrieved}). Next link: {(string.IsNullOrEmpty(nextLink) ? "none" : "present")}");
                    }

                    System.Diagnostics.Debug.WriteLine(
                        $"Search parameter {searchParameterCode} is working - search executed successfully without 'NotSupported' error on attempt {attempt}. Total pages fetched: {pageCount}, Total entries retrieved: {totalEntriesRetrieved}");

                    // Validate record expectations if specified
                    if (!string.IsNullOrEmpty(expectedResourceType))
                    {
                        var resourcesFound = allResourcesFound
                            .Where(e => e.Resource?.TypeName == expectedResourceType)
                            .ToList();

                        if (shouldFindRecords)
                        {
                            Assert.NotEmpty(resourcesFound);
                            Assert.True(
                                resourcesFound.Count > 0,
                                $"Expected to find {expectedResourceType} records for search parameter {searchParameterCode}, but none were returned.");

                            System.Diagnostics.Debug.WriteLine(
                                $"Search parameter {searchParameterCode} correctly returned {resourcesFound.Count} {expectedResourceType} record(s) across {pageCount} page(s)");
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
                    System.Diagnostics.Debug.WriteLine($"Search query: {searchQuery} executed successfully on attempt {attempt} across {pageCount} page(s).");

                    // Success - return without retrying
                    return;
                }
                catch (Exception ex)
                {
                    lastException = ex;

                    if (attempt < maxRetries)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"Search parameter {searchParameterCode} verification failed on attempt {attempt} of {maxRetries}. " +
                            $"Retrying in {retryDelayMs}ms. Error: {ex.Message}");

                        await Task.Delay(retryDelayMs);
                    }
                }
            }

            // All retries exhausted, throw the last exception with context
            throw new Xunit.Sdk.XunitException(
                $"Search parameter {searchParameterCode} should be usable after reindex (failed after {maxRetries} attempts). Error: {lastException?.Message}",
                lastException);
        }

        /// <summary>
        /// Sets up test data by creating the specified number of resources.
        /// Returns the list of created resource IDs for cleanup.
        /// Uses parallel individual creates for improved performance with retry logic.
        /// </summary>
        /// <param name="resourceType">The FHIR resource type to create (e.g., "Person", "Specimen")</param>
        /// <param name="desiredCount">The number of resources to create</param>
        /// <param name="randomSuffix">Random suffix for unique resource IDs</param>
        /// <param name="createResourceFunc">Function to create a single resource given an ID and name</param>
        /// <returns>A tuple containing the list of created resource IDs and the count of successfully created resources</returns>
        private async Task<(List<(string resourceType, string resourceId)> createdResources, int finalCount)> SetupTestDataAsync<T>(
            string resourceType,
            int desiredCount,
            string randomSuffix,
            Func<string, string, Task<T>> createResourceFunc)
            where T : Resource
        {
            var createdResources = new List<(string resourceType, string resourceId)>();

            System.Diagnostics.Debug.WriteLine($"Creating {desiredCount} new {resourceType} resources...");

            // Create resources in batches using parallel individual creates for better performance
            const int batchSize = 500; // Process 500 resources at a time in parallel
            const int maxCreateRetries = 3; // Retry failed creates up to 3 times
            int totalCreated = 0;
            var failedIds = new List<string>();

            for (int batchStart = 0; batchStart < desiredCount; batchStart += batchSize)
            {
                int currentBatchSize = Math.Min(batchSize, desiredCount - batchStart);

                // Track which resources in this batch need to be created/retried
                var resourcesToCreateInBatch = new List<(int index, string id, string name)>();
                for (int i = 0; i < currentBatchSize; i++)
                {
                    int index = batchStart + i;
                    string id = $"{resourceType.ToLowerInvariant()}-{randomSuffix}-{index}";
                    string name = $"Test {resourceType} {index}";
                    resourcesToCreateInBatch.Add((index, id, name));
                }

                // Retry failed creates up to maxCreateRetries times
                for (int retryAttempt = 0; retryAttempt < maxCreateRetries && resourcesToCreateInBatch.Any(); retryAttempt++)
                {
                    if (retryAttempt > 0)
                    {
                        // Exponential backoff for retries
                        var delayMs = 1000 * (int)Math.Pow(2, retryAttempt - 1); // 1s, 2s, 4s
                        System.Diagnostics.Debug.WriteLine($"Retrying {resourcesToCreateInBatch.Count} failed resources after {delayMs}ms delay (attempt {retryAttempt + 1}/{maxCreateRetries})");
                        await Task.Delay(delayMs);
                    }

                    var batchTasks = resourcesToCreateInBatch
                        .Select(r => CreateAndPostResourceWithStatusAsync(r.id, r.name, createResourceFunc))
                        .ToList();

                    try
                    {
                        // Execute all creates in parallel
                        var results = await Task.WhenAll(batchTasks);

                        // Track successes and failures
                        var nextRetryBatch = new List<(int index, string id, string name)>();
                        for (int i = 0; i < results.Length; i++)
                        {
                            var (success, resource, id) = results[i];
                            var originalResource = resourcesToCreateInBatch[i];

                            if (success && resource != null && !string.IsNullOrEmpty(resource.Id))
                            {
                                createdResources.Add((resourceType, resource.Id));
                                totalCreated++;
                            }
                            else
                            {
                                // Queue for retry if we haven't exhausted retries
                                if (retryAttempt < maxCreateRetries - 1)
                                {
                                    nextRetryBatch.Add(originalResource);
                                }
                                else
                                {
                                    // Final failure after all retries
                                    failedIds.Add(id);
                                }
                            }
                        }

                        resourcesToCreateInBatch = nextRetryBatch;

                        System.Diagnostics.Debug.WriteLine(
                            $"Batch {(batchStart / batchSize) + 1} attempt {retryAttempt + 1}: " +
                            $"{totalCreated} total created, {resourcesToCreateInBatch.Count} pending retry, " +
                            $"{failedIds.Count} permanently failed");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to create batch at offset {batchStart}: {ex.Message}");
                        if (retryAttempt == maxCreateRetries - 1)
                        {
                            // Add all remaining to failed list
                            failedIds.AddRange(resourcesToCreateInBatch.Select(r => r.id));
                        }
                    }
                }

                // Delay between batches to avoid overwhelming the server
                if (batchStart + batchSize < desiredCount)
                {
                    await Task.Delay(500);
                }
            }

            // Report on any failures
            if (failedIds.Any())
            {
                System.Diagnostics.Debug.WriteLine($"WARNING: {failedIds.Count} resources failed to create after {maxCreateRetries} retries");
                System.Diagnostics.Debug.WriteLine($"Failed IDs (first 10): {string.Join(", ", failedIds.Take(10))}");
            }

            // Calculate acceptable threshold (allow 5% failure rate for transient issues)
            var acceptableMinimum = (int)(desiredCount * 0.95);

            // Assert that we created enough resources
            if (totalCreated < acceptableMinimum)
            {
                var errorMsg = $"CRITICAL: Failed to create sufficient {resourceType} resources. " +
                              $"Desired: {desiredCount}, Acceptable minimum: {acceptableMinimum}, " +
                              $"Successfully created: {totalCreated}, Failed: {failedIds.Count}";
                System.Diagnostics.Debug.WriteLine(errorMsg);
                Assert.Fail(errorMsg);
            }
            else if (totalCreated < desiredCount)
            {
                // Log warning but don't fail
                System.Diagnostics.Debug.WriteLine(
                    $"WARNING: Created {totalCreated}/{desiredCount} {resourceType} resources " +
                    $"(within acceptable threshold of {acceptableMinimum})");
            }

            System.Diagnostics.Debug.WriteLine($"Successfully created {totalCreated} new {resourceType} resources.");

            // Return the ACTUAL count of resources we created and have IDs for
            return (createdResources, totalCreated);
        }

        private async Task RandomPersonUpdate(IList<(string resourceType, string resourceId)> resources)
        {
            foreach (var resource in resources.OrderBy(_ => RandomNumberGenerator.GetInt32((int)1e6)))
            {
                await _fixture.TestFhirClient.UpdateAsync(CreatePersonResource(resource.resourceId, Guid.NewGuid().ToString()));
            }
        }

        private async Task CheckCounts(Uri jobUri, long expectedTotal, long expectedSuccesses, bool lessThan)
        {
            var response = await _fixture.TestFhirClient.HttpClient.GetAsync(jobUri, CancellationToken.None);
            var content = await response.Content.ReadAsStringAsync();
            var parameters = new Hl7.Fhir.Serialization.FhirJsonParser().Parse<Parameters>(content);
            var total = (long)((FhirDecimal)parameters.Parameter.FirstOrDefault(p => p.Name == "totalResourcesToReindex").Value).Value;
            if (lessThan)
            {
                Assert.True(total < expectedTotal);
            }
            else
            {
                Assert.Equal(expectedTotal, total);
            }

            var successes = (long)((FhirDecimal)parameters.Parameter.FirstOrDefault(p => p.Name == "resourcesSuccessfullyReindexed").Value).Value;
            if (lessThan)
            {
                Assert.True(successes < expectedSuccesses);
            }
            else
            {
                Assert.Equal(expectedSuccesses, successes);
            }

            Assert.Equal(total, successes);
        }

        /// <summary>
        /// Checks for any running reindex jobs and cancels them before starting a new test.
        /// This ensures test isolation and prevents conflicts between concurrent reindex operations.
        /// Uses GET $reindex to check for active jobs (works for both SQL Server and Cosmos DB).
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        private async Task CancelAnyRunningReindexJobsAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("Checking for any running reindex jobs via GET $reindex...");

                // Use GET to $reindex to check for active jobs
                var response = await _fixture.TestFhirClient.HttpClient.GetAsync("$reindex", cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    System.Diagnostics.Debug.WriteLine($"GET $reindex returned non-success status: {response.StatusCode}. No active jobs to cancel.");
                    return;
                }

                // Parse the response as a Parameters resource
                var content = await response.Content.ReadAsStringAsync();

                // Deserialize the Parameters resource
                Parameters parameters = null;
                try
                {
                    // Use the FHIR parser to deserialize the response
                    var parser = new Hl7.Fhir.Serialization.FhirJsonParser();
                    parameters = parser.Parse<Parameters>(content);
                }
                catch (Exception parseEx)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to parse $reindex response as Parameters: {parseEx.Message}");
                    return;
                }

                if (parameters?.Parameter == null || !parameters.Parameter.Any())
                {
                    System.Diagnostics.Debug.WriteLine("No parameters found in $reindex response. No active jobs to cancel.");
                    return;
                }

                // Extract job ID and status from the parameters
                var idParam = parameters.Parameter.FirstOrDefault(p => p.Name == "id");
                var statusParam = parameters.Parameter.FirstOrDefault(p => p.Name == "status");

                if (idParam?.Value == null)
                {
                    System.Diagnostics.Debug.WriteLine("No job ID found in $reindex response. No active jobs to cancel.");
                    return;
                }

                // Extract the job ID
                string jobId = null;
                if (idParam.Value is FhirString fhirString)
                {
                    jobId = fhirString.Value;
                }
                else if (idParam.Value is Integer integer)
                {
                    jobId = integer.Value.ToString();
                }
                else
                {
                    jobId = idParam.Value.ToString();
                }

                if (string.IsNullOrEmpty(jobId))
                {
                    System.Diagnostics.Debug.WriteLine("Job ID is empty. No active jobs to cancel.");
                    return;
                }

                // Job is active (Running or Queued), cancel it
                System.Diagnostics.Debug.WriteLine($"Job {jobId} is active. Attempting to cancel...");

                // Use the correct URI format: /_operations/reindex/{jobId}
                var jobUri = new Uri($"{_fixture.TestFhirClient.HttpClient.BaseAddress}_operations/reindex/{jobId}");

                // Send DELETE request to cancel the job
                using var deleteRequest = new System.Net.Http.HttpRequestMessage
                {
                    Method = System.Net.Http.HttpMethod.Delete,
                    RequestUri = jobUri,
                };

                var cancelResponse = await _fixture.TestFhirClient.HttpClient.SendAsync(deleteRequest, cancellationToken);
                System.Diagnostics.Debug.WriteLine($"Cancel request for job {jobId} completed with status: {cancelResponse.StatusCode}");

                // Wait for the job to reach a terminal status
                if (cancelResponse.IsSuccessStatusCode)
                {
                    System.Diagnostics.Debug.WriteLine($"Waiting for job {jobId} to reach terminal state...");
                    var finalStatus = await WaitForJobCompletionAsync(jobUri, TimeSpan.FromSeconds(120));
                    System.Diagnostics.Debug.WriteLine($"Job {jobId} reached final status: {finalStatus}");

                    // Add a small delay to ensure system is ready
                    await Task.Delay(2000, cancellationToken);
                }

                System.Diagnostics.Debug.WriteLine("Completed checking and canceling running reindex jobs");
            }
            catch (Exception ex)
            {
                // Log but don't fail - this is a cleanup/safety check
                System.Diagnostics.Debug.WriteLine($"Error in CancelAnyRunningReindexJobsAsync: {ex.Message}");
            }
        }
    }
}
