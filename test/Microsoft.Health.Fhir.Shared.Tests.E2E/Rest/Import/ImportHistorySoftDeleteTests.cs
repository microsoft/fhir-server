// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Import
{
    [Trait(Traits.OwningTeam, OwningTeam.FhirImport)]
    [Trait(Traits.Category, Categories.Import)]
    [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer, Format.Json)]
    public class ImportHistorySoftDeleteTests : IClassFixture<ImportHistorySoftDeleteTestFixture>
    {
        private readonly TestFhirClient _client;
        private readonly ImportHistorySoftDeleteTestFixture _fixture;

        public ImportHistorySoftDeleteTests(ImportHistorySoftDeleteTestFixture fixture)
        {
            _client = fixture.TestFhirClient;
            _fixture = fixture;
        }

        [Fact]
        public async Task GivenImportedResourceOverExisting_WhenImportVersionNotSpecified_ThenBothVersionsExistCorrectly()
        {
            // Validate that the new version of the resource is returned by a general search.
            var resourceId = _fixture.NewImplicitVersionIdResources.Import[0].Id;
            var queryById = $"Patient?_id={resourceId}&_tag={_fixture.FixtureTag}";

            Bundle result = await _client.SearchAsync(queryById);

            // No resources should be imported since the version id already exists.
            Assert.Empty(result.Entry);

            // Validate that the old version of the resource is returned by a history search.
            var queryByIdHistory = $"Patient/{resourceId}/_history";
            result = await _client.SearchAsync(queryByIdHistory);

            // Manual assert - can be removed before merging
            Assert.Equal("1", result.Entry.First().Resource.VersionId);
            Assert.Equal(_fixture.NewImplicitVersionIdResources.Import[0].Meta.LastUpdated, result.Entry.First().Resource.Meta.LastUpdated);

            ImportTestHelper.VerifyBundleWithMeta(
                result,
                _fixture.NewImplicitVersionIdResources.Import.Concat(_fixture.NewImplicitVersionIdResources.Existing).ToArray());
        }

        [Fact]
        public async Task GivenImportedResourceOverExisting_WhenImportVersionInConflict_ThenOnlyOneResourceOnServer()
        {
            // Validate that the new version of the resource is returned by a general search.
            var resourceId = _fixture.ConflictingVersionResources.Import.Select(r => r.Id).Distinct().Single();
            var queryById = $"Patient?_id={resourceId}&_tag={_fixture.FixtureTag}";
            Bundle result = await _client.SearchAsync(queryById);

            // No resources should be imported since the version id already exists.
            Assert.Empty(result.Entry);

            // Validate that the old version of the resource is returned by a history search.
            var queryByIdHistory = $"Patient/{resourceId}/_history";
            result = await _client.SearchAsync(queryByIdHistory);

            // Manual assert - can be removed before merging
            Assert.Equal("1", result.Entry.First().Resource.VersionId);
            Assert.Equal(
                _fixture.ConflictingVersionResources.Existing[0].Meta.LastUpdated,
                result.Entry.First().Resource.Meta.LastUpdated);

            ImportTestHelper.VerifyBundleWithMeta(
                result,
                _fixture.ConflictingVersionResources.Existing.ToArray());
        }

        [Fact]
        public async Task GivenImportWithCreateAndDelete_WhenDeleteAfterCreate_ThenBothVersionsExistCorrectly()
        {
            // Validate that the new version of the resource is returned by a general search.
            var resourceIds = _fixture.ImportAndDeleteResources.Import.Select(r => r.Id).Distinct().ToArray();
            var queryByIds = $"Patient?_id={string.Join(",", resourceIds)}&_tag={_fixture.FixtureTag}";

            List<Resource> expectedNonHistorical = new()
            {
                _fixture.ImportAndDeleteResources.Import[1],
                _fixture.ImportAndDeleteResources.Import[3],
                _fixture.ImportAndDeleteResources.Import[5],
                _fixture.ImportAndDeleteResources.Import[7],
            };

            List<Resource> expectedNonHistoricalMetaMatching = new()
            {
                _fixture.ImportAndDeleteResources.Import[1],
                _fixture.ImportAndDeleteResources.Import[3],

                // We cannot expect both versions of explicitVersionImplicitUpdatedGuid to exist since import cannot infer the order without lastUpdated.
                // We cannot expect both versions of implicitVersionUpdatedGuid to exist since import cannot infer the order without lastUpdated.
            };

            Bundle result = await _fixture.TestFhirClient.SearchAsync(queryByIds);
            ImportTestHelper.VerifyBundle(result, expectedNonHistorical.ToArray());
            ImportTestHelper.VerifyBundleWithMeta(result, expectedNonHistoricalMetaMatching.ToArray());

            // Validate all versions
            await ImportTestHelper.VerifyHistoryResultAsync(_fixture.TestFhirClient, _fixture.ImportAndDeleteResources.Import.ToArray());
        }

        /*
        [Fact]
        public async Task GivenImportedResourcesWithBasicSoftDelete_WhenSearchedById_ThenNoResourceIsReturned()
        {
            // Validate that the soft deleted resource is not returned by a general search.
            var queryById = $"Patient?_id={_fixture.ExistingServerResources["NewImplicitVersionId"].Id}&_tag={_fixture.FixtureTag}";
            await ImportTestHelper.VerifySearchResultAsync(_fixture.TestFhirClient, queryById, new Resource[0]);
        }

        [Fact]
        public async Task GivenImportedResourcesWithBasicSoftDelete_WhenSearchedById_ThenNoResourceIsReturned()
        {
            // Validate that the soft deleted resource is not returned by a general search.
            var queryById = $"Patient?_id={_fixture.ExistingServerResources["NewImplicitVersionId"].Id}&_tag={_fixture.FixtureTag}";
            await ImportTestHelper.VerifySearchResultAsync(_fixture.TestFhirClient, queryById, new Resource[0]);
        }

        [Fact]
        public async Task GivenImportedResourcesWithBasicSoftDelete_WhenHistoryIsSearched_ThenAllVersionsReturned()
        {
            // Validate that the soft deleted resource IS returned by a history search.
            var queryByIdHistory = $"Patient/{_fixture.PatientSimpleSoftDelete[0].Id}/_history";
            Bundle result = await _client.SearchAsync(queryByIdHistory);

            // #TODO - update stylecop settings to allow spread operator
            ImportTestHelper.VerifyBundle(result, _fixture.PatientSimpleSoftDelete.ToArray());

            Console.WriteLine(result.Entry[0].Resource.Meta.VersionId);
            Console.WriteLine(result.Entry[1].Resource.Meta.VersionId);
            Console.WriteLine("poop");
        }
        */
    }
}
