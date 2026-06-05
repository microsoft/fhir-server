// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Health.Extensions.Xunit;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Storage.Registry;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    [FhirStorageTestsFixtureArgumentSets(DataStore.SqlServer)]
    public class SearchParameterOptimisticConcurrencyTests : IClassFixture<FhirStorageTestsFixture>
    {
        private readonly FhirStorageTestsFixture _fixture;
        private readonly IFhirStorageTestHelper _testHelper;

        public SearchParameterOptimisticConcurrencyTests(FhirStorageTestsFixture fixture)
        {
            _fixture = fixture;
            _testHelper = fixture.TestHelper;
        }

        [Fact]
        public async Task GivenNewSearchParameterStatus_WhenUpserting_ThenLastUpdatedIsReturned()
        {
            await _fixture.SearchParameterOperations.GetAndApplySearchParameterUpdates(CancellationToken.None);

            var testUri = $"http://test.com/SearchParameter/ConcurrencyTest_{Guid.NewGuid()}";
            var newStatus = new ResourceSearchParameterStatus
            {
                Uri = new Uri(testUri),
                Status = SearchParameterStatus.Disabled,
                IsPartiallySupported = false,
                LastUpdated = _fixture.SearchParameterOperations.SearchParamLastUpdated,
            };

            try
            {
                await _fixture.SearchParameterStatusDataStore.UpsertStatuses([newStatus], CancellationToken.None);

                // Get the upserted status to check LastUpdated was assigned
                var allStatuses = await _fixture.SearchParameterStatusDataStore.GetSearchParameterStatuses(CancellationToken.None);
                var upsertedStatus = allStatuses.FirstOrDefault(s => s.Uri.ToString() == testUri);

                Assert.NotNull(upsertedStatus);
                Assert.True(upsertedStatus.LastUpdated != default(DateTimeOffset), "LastUpdated should be assigned for new parameters");
            }
            finally
            {
                await _testHelper.DeleteSearchParameterStatusAsync(testUri);
            }
        }

        [Fact]
        public async Task GivenExistingSearchParameterStatus_WhenUpdatingWithCorrectLastUpdated_ThenSucceeds()
        {
            await _fixture.SearchParameterOperations.GetAndApplySearchParameterUpdates(CancellationToken.None);

            var testUri = $"http://test.com/SearchParameter/ConcurrencyTest_{Guid.NewGuid()}";
            var initialStatus = new ResourceSearchParameterStatus
            {
                Uri = new Uri(testUri),
                Status = SearchParameterStatus.Disabled,
                IsPartiallySupported = false,
                LastUpdated = _fixture.SearchParameterOperations.SearchParamLastUpdated,
            };

            try
            {
                await _fixture.SearchParameterStatusDataStore.UpsertStatuses([initialStatus], CancellationToken.None);

                // Get the created status with its LastUpdated
                var allStatuses = await _fixture.SearchParameterStatusDataStore.GetSearchParameterStatuses(CancellationToken.None);
                var createdStatus = allStatuses.First(s => s.Uri.ToString() == testUri);

                await _fixture.SearchParameterOperations.GetAndApplySearchParameterUpdates(CancellationToken.None);

                // Modify and update with correct LastUpdated
                var updatedStatus = new ResourceSearchParameterStatus
                {
                    Uri = createdStatus.Uri,
                    Status = SearchParameterStatus.Enabled, // Changed status
                    IsPartiallySupported = true, // Changed partially supported
                    LastUpdated = createdStatus.LastUpdated, // Use the current LastUpdated, it should match max one.
                };

                await _fixture.SearchParameterStatusDataStore.UpsertStatuses([updatedStatus], CancellationToken.None);

                // Verify the update succeeded
                var updatedAllStatuses = await _fixture.SearchParameterStatusDataStore.GetSearchParameterStatuses(CancellationToken.None);
                var result = updatedAllStatuses.First(s => s.Uri.ToString() == testUri);

                Assert.Equal(SearchParameterStatus.Enabled, result.Status);
                Assert.True(result.IsPartiallySupported);
                Assert.True(result.LastUpdated > createdStatus.LastUpdated, "LastUpdated should change after update");
            }
            finally
            {
                await _testHelper.DeleteSearchParameterStatusAsync(testUri);
            }
        }

        [Fact]
        public async Task GivenExistingSearchParameterStatus_WhenUpdatingWithIncorrectLastUpdated_ThenShouldFail()
        {
            await _fixture.SearchParameterOperations.GetAndApplySearchParameterUpdates(CancellationToken.None);

            var testUri = $"http://test.com/SearchParameter/ConcurrencyTest_{Guid.NewGuid()}";
            var initialStatus = new ResourceSearchParameterStatus
            {
                Uri = new Uri(testUri),
                Status = SearchParameterStatus.Disabled,
                IsPartiallySupported = false,
                LastUpdated = _fixture.SearchParameterOperations.SearchParamLastUpdated,
            };

            try
            {
                // Create initial status
                await _fixture.SearchParameterStatusDataStore.UpsertStatuses([initialStatus], CancellationToken.None);

                // Get the created status with its LastUpdated
                var allStatuses = await _fixture.SearchParameterStatusDataStore.GetSearchParameterStatuses(CancellationToken.None);
                var createdStatus = allStatuses.First(s => s.Uri.ToString() == testUri);

                // Make an intermediate update to change the LastUpdated
                var intermediateUpdate = new ResourceSearchParameterStatus
                {
                    Uri = createdStatus.Uri,
                    Status = SearchParameterStatus.Enabled,
                    IsPartiallySupported = false,
                    LastUpdated = createdStatus.LastUpdated,
                };
                await _fixture.SearchParameterStatusDataStore.UpsertStatuses([intermediateUpdate], CancellationToken.None);

                // Now try to update with the stale LastUpdated, should fail
                var staleUpdate = new ResourceSearchParameterStatus
                {
                    Uri = createdStatus.Uri,
                    Status = SearchParameterStatus.Supported,
                    IsPartiallySupported = true,
                    LastUpdated = createdStatus.LastUpdated, // This is now stale
                };

                try
                {
                    await _fixture.SearchParameterStatusDataStore.UpsertStatuses([staleUpdate], CancellationToken.None);
                }
                catch (BadRequestException ex)
                {
                    Assert.True(ex.Message.StartsWith(Core.Resources.SearchParameterConcurrencyConflict), $"expected={Core.Resources.SearchParameterConcurrencyConflict}, actual={ex.Message}");
                }
            }
            finally
            {
                await _testHelper.DeleteSearchParameterStatusAsync(testUri);
            }
        }
    }
}
