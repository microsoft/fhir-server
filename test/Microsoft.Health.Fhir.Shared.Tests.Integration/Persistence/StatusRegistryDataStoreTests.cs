﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    [FhirStorageTestsFixtureArgumentSets(DataStore.All)]
    public class StatusRegistryDataStoreTests : IClassFixture<FhirStorageTestsFixture>
    {
        private readonly FhirStorageTestsFixture _fixture;
        private readonly IFhirStorageTestHelper _testHelper;

        public StatusRegistryDataStoreTests(FhirStorageTestsFixture fixture)
        {
            _fixture = fixture;
            _testHelper = fixture.TestHelper;
        }

        [Fact]
        public async Task GivenAStatusRegistry_WhenGettingStatuses_ThenTheStatusesAreRetrieved()
        {
            IReadOnlyCollection<ResourceSearchParameterStatus> expectedStatuses = await _fixture.FilebasedSearchParameterRegistry.GetSearchParameterStatuses();
            IReadOnlyCollection<ResourceSearchParameterStatus> actualStatuses = await _fixture.SearchParameterRegistry.GetSearchParameterStatuses();

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

            IReadOnlyCollection<ResourceSearchParameterStatus> readonlyStatusesBeforeUpsert = await _fixture.SearchParameterRegistry.GetSearchParameterStatuses();
            var expectedStatuses = readonlyStatusesBeforeUpsert.ToList();
            expectedStatuses.Add(status1);
            expectedStatuses.Add(status2);

            var newStatusesToUpsert = new List<ResourceSearchParameterStatus>();
            newStatusesToUpsert.Add(status1);
            newStatusesToUpsert.Add(status2);

            try
            {
                await _fixture.SearchParameterRegistry.UpsertStatuses(newStatusesToUpsert);

                IReadOnlyCollection<ResourceSearchParameterStatus> actualStatuses = await _fixture.SearchParameterRegistry.GetSearchParameterStatuses();

                ValidateSearchParameterStatuses(expectedStatuses, actualStatuses);
            }
            finally
            {
                await _testHelper.DeleteSearchParameterStatusAsync(statusName1);
                await _testHelper.DeleteSearchParameterStatusAsync(statusName2);
            }
        }

        private static void ValidateSearchParameterStatuses(IReadOnlyCollection<ResourceSearchParameterStatus> expectedStatuses, IReadOnlyCollection<ResourceSearchParameterStatus> actualStatuses)
        {
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
