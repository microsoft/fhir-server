// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    [FhirStorageTestsFixtureArgumentSets(DataStore.All)]
    public class SearchParameterStatusDataStoreTests : IClassFixture<FhirStorageTestsFixture>
    {
        private readonly FhirStorageTestsFixture _fixture;
        private readonly IFhirStorageTestHelper _testHelper;

        public SearchParameterStatusDataStoreTests(FhirStorageTestsFixture fixture)
        {
            _fixture = fixture;
            _testHelper = fixture.TestHelper;
        }

        [Fact]
        public async Task GivenAStatusRegistry_WhenGettingStatuses_ThenTheStatusesAreRetrieved()
        {
            IReadOnlyCollection<ResourceSearchParameterStatus> expectedStatuses = await _fixture.FilebasedSearchParameterStatusDataStore.GetSearchParameterStatuses(CancellationToken.None);
            IReadOnlyCollection<ResourceSearchParameterStatus> actualStatuses = await _fixture.SearchParameterStatusDataStore.GetSearchParameterStatuses(CancellationToken.None);

            ValidateSearchParameterStatuses(expectedStatuses, actualStatuses);
        }

        [Fact]
        public async Task GivenAStatusRegistry_WhenUpsertingNewStatuses_ThenTheStatusesAreAdded()
        {
            string statusName1 = "http://hl7.org/fhir/SearchParameter/Test-1";
            string statusName2 = "http://hl7.org/fhir/SearchParameter/Test-2";

            var status1 = new ResourceSearchParameterStatus
            {
                Uri = new Uri(statusName1), Status = SearchParameterStatus.Disabled, IsPartiallySupported = false,
            };

            var status2 = new ResourceSearchParameterStatus
            {
                Uri = new Uri(statusName2), Status = SearchParameterStatus.Disabled, IsPartiallySupported = false,
            };

            IReadOnlyCollection<ResourceSearchParameterStatus> readonlyStatusesBeforeUpsert = await _fixture.SearchParameterStatusDataStore.GetSearchParameterStatuses(CancellationToken.None);
            var expectedStatuses = readonlyStatusesBeforeUpsert.ToList();
            expectedStatuses.Add(status1);
            expectedStatuses.Add(status2);

            var statusesToUpsert = new List<ResourceSearchParameterStatus> { status1, status2 };

            try
            {
                await _fixture.SearchParameterStatusDataStore.UpsertStatuses(statusesToUpsert, CancellationToken.None);

                IReadOnlyCollection<ResourceSearchParameterStatus> actualStatuses = await _fixture.SearchParameterStatusDataStore.GetSearchParameterStatuses(CancellationToken.None);

                ValidateSearchParameterStatuses(expectedStatuses, actualStatuses);
            }
            finally
            {
                await _testHelper.DeleteSearchParameterStatusAsync(statusName1);
                await _testHelper.DeleteSearchParameterStatusAsync(statusName2);
            }
        }

        [Fact]
        public async Task GivenAStatusRegistry_WhenUpsertingExistingStatuses_ThenTheExistingStatusesAreUpdated()
        {
            IReadOnlyCollection<ResourceSearchParameterStatus> statusesBeforeUpdate = await _fixture.SearchParameterStatusDataStore.GetSearchParameterStatuses(CancellationToken.None);

            // Get two existing statuses.
            ResourceSearchParameterStatus expectedStatus1 = statusesBeforeUpdate.First();
            ResourceSearchParameterStatus expectedStatus2 = statusesBeforeUpdate.Last();

            // Modify them in some way.
            expectedStatus1.IsPartiallySupported = !expectedStatus1.IsPartiallySupported;
            expectedStatus2.IsPartiallySupported = !expectedStatus2.IsPartiallySupported;

            var statusesToUpsert = new List<ResourceSearchParameterStatus> { expectedStatus1, expectedStatus2 };

            try
            {
                // Upsert the two existing, modified statuses.
                await _fixture.SearchParameterStatusDataStore.UpsertStatuses(statusesToUpsert, CancellationToken.None);

                IReadOnlyCollection<ResourceSearchParameterStatus> statusesAfterUpdate =
                    await _fixture.SearchParameterStatusDataStore.GetSearchParameterStatuses(CancellationToken.None);

                Assert.Equal(statusesBeforeUpdate.Count, statusesAfterUpdate.Count);

                ResourceSearchParameterStatus actualStatus1 = statusesAfterUpdate.FirstOrDefault(s => s.Uri.Equals(expectedStatus1.Uri));
                ResourceSearchParameterStatus actualStatus2 = statusesAfterUpdate.FirstOrDefault(s => s.Uri.Equals(expectedStatus2.Uri));

                Assert.NotNull(actualStatus1);
                Assert.NotNull(actualStatus2);

                Assert.Equal(expectedStatus1.Status, actualStatus1.Status);
                Assert.Equal(expectedStatus1.IsPartiallySupported, actualStatus1.IsPartiallySupported);

                Assert.Equal(expectedStatus2.Status, actualStatus2.Status);
                Assert.Equal(expectedStatus2.IsPartiallySupported, actualStatus2.IsPartiallySupported);
            }
            finally
            {
                // Reset changes made.
                expectedStatus1.IsPartiallySupported = !expectedStatus1.IsPartiallySupported;
                expectedStatus2.IsPartiallySupported = !expectedStatus2.IsPartiallySupported;

                statusesToUpsert = new List<ResourceSearchParameterStatus> { expectedStatus1, expectedStatus2 };

                await _fixture.SearchParameterStatusDataStore.UpsertStatuses(statusesToUpsert, CancellationToken.None);
            }
        }

        private static void ValidateSearchParameterStatuses(IReadOnlyCollection<ResourceSearchParameterStatus> expectedStatuses, IReadOnlyCollection<ResourceSearchParameterStatus> actualStatuses)
        {
            Assert.NotEmpty(expectedStatuses);

            var sortedExpected = expectedStatuses.OrderBy(status => status.Uri.ToString()).ToList();
            var sortedActual = actualStatuses.OrderBy(status => status.Uri.ToString()).ToList();

            Assert.Equal(sortedExpected.Count, sortedActual.Count);

            for (int i = 0; i < sortedExpected.Count; i++)
            {
                Assert.Equal(sortedExpected[i].Uri, sortedActual[i].Uri);
                Assert.Equal(sortedExpected[i].Status, sortedActual[i].Status);
                Assert.Equal(sortedExpected[i].IsPartiallySupported, sortedActual[i].IsPartiallySupported);
            }
        }
    }
}
