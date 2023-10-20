// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Models;
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
            var resourceId = _fixture.TestResources["NewImplicitVersionId"].Import[0].Id;
            var queryById = $"Patient?_id={resourceId}&_tag={_fixture.FixtureTag}";

            Bundle result = await _client.SearchAsync(queryById);

            // Only one resource should be returned (same id) new version
            Assert.Single(result.Entry);
            Assert.Equal("2", result.Entry.Single().Resource.Meta.VersionId);

            // Validate that the old version of the resource is returned by a history search.
            var queryByIdHistory = $"Patient/{resourceId}/_history";
            result = await _client.SearchAsync(queryByIdHistory);

            // Inform the test which version id for asserting
            _fixture.TestResources["NewImplicitVersionId"].Import[0].Meta.VersionId = "2";
            var expectedResources = _fixture.TestResources["NewImplicitVersionId"].Import
                .Concat(_fixture.TestResources["NewImplicitVersionId"].Existing);

            // Both versions should be returned by the history endpoint
            ImportTestHelper.VerifyBundleWithMeta(result, expectedResources.ToArray());
        }

        [Fact]
        public async Task GivenImportedResourceOverExisting_WhenImportVersionInConflict_ThenOnlyOneResourceOnServer()
        {
            // Validate that the new version of the resource is returned by a general search.
            var resourceId = _fixture.TestResources["ImportOverExistingVersionId"].Import.Select(r => r.Id).Distinct().Single();
            var queryById = $"Patient?_id={resourceId}&_tag={_fixture.FixtureTag}";
            Bundle result = await _client.SearchAsync(queryById);

            // No resources should be imported since the version id already exists.
            Assert.Empty(result.Entry);

            // Validate that the existing version of the resource is returned by a history search.
            var queryByIdHistory = $"Patient/{resourceId}/_history";
            result = await _client.SearchAsync(queryByIdHistory);
            Assert.Equal("1", result.Entry.First().Resource.VersionId);
            Assert.Equal(
                _fixture.TestResources["ImportOverExistingVersionId"].Existing[0].Meta.LastUpdated,
                result.Entry.First().Resource.Meta.LastUpdated);
            Assert.Single(result.Entry);
        }

        [Fact]
        public async Task GivenImportedWithDuplicateVersionId_WhenImportVersionInConflict_ThenOnlyOneResourceOnServer()
        {
            // Validate that the new version of the resource is returned by a general search.
            var resourceId = _fixture.TestResources["ImportWithSameVersionId"].Import.Select(r => r.Id).Distinct().Single();
            var queryById = $"Patient?_id={resourceId}&_tag={_fixture.FixtureTag}";
            Bundle result = await _client.SearchAsync(queryById);

            // Only a single version of the resource should be imported
            Assert.Single(result.Entry);

            // Validate a single version of the resource is returned by a history search.
            var queryByIdHistory = $"Patient/{resourceId}/_history";
            result = await _client.SearchAsync(queryByIdHistory);
            Assert.True(result.Entry.Count == 1);
        }

        [Fact]
        public async Task GivenImportWithCreateAndDelete_WhenImportedWithLastUpdatedAndVersion_ThenBothVersionsExist()
        {
            var testResources = _fixture.TestResources["ImportAndDeleteExplicitVersionUpdate"].Import;

            // Validate that the new version of the resource is returned by a general search.
            var resourceIds = testResources.Select(r => r.Id).Distinct().ToArray();
            var queryByIds = $"Patient?_id={string.Join(",", resourceIds)}&_tag={_fixture.FixtureTag}";

            Bundle result = await _fixture.TestFhirClient.SearchAsync(queryByIds);

            // Since both last updated and version are specified, the last version will be persisted regardless of order.
            var expectedNonHistorical = testResources
                .Where(r => r.Meta.VersionId == "2")
                .Where(r => !r.Meta.Extension.Any(e => e.Url == KnownFhirPaths.AzureSoftDeletedExtensionUrl));

            ImportTestHelper.VerifyBundle(result, expectedNonHistorical.ToArray());

            // Since both last updated and version are specified, both resources should be imported in all cases.
            List<Resource> expectedHistorical = testResources;

            await ImportTestHelper.VerifyHistoryResultAsync(_fixture.TestFhirClient, testResources.ToArray());
        }

        [Fact]
        public async Task GivenImportWithCreateAndDelete_WhenImportedWithoutLastUpdatedAndVersion_ThenFirstInFileVersionsExist()
        {
            // Setup test resources. They have been imported without version id/last updated so we need to add these for the test.
            var testResources = _fixture.TestResources["ImportAndDeleteImplicitVersionUpdated"].Import;

            // We expect only the first resource to be imported. Validating the non-deleted resources are returned.
            List<Resource> expectedNonHistorical = testResources
                .GroupBy(x => x.Id)
                .Select(x => x.First())
                .Where(x => !x.Meta.Extension.Any(e => e.Url == KnownFhirPaths.AzureSoftDeletedExtensionUrl))
                .ToList();

            // Since only one version is imported, this will always be 1.
            foreach (var resource in expectedNonHistorical)
            {
                if (resource.Meta.VersionId is null || resource.Meta.LastUpdated is null)
                {
                    resource.Meta.VersionId = "1";
                }

                if (resource.Meta.LastUpdated is null)
                {
                    Bundle serverResponse = (await _fixture.TestFhirClient.SearchAsync($"/Patient/{resource.Id}/_history")).Resource;
                    resource.Meta.LastUpdated = serverResponse.Entry.First().Resource.Meta.LastUpdated;
                }
            }

            // Query the resource endpoint and ensure only non-deleted resources are returned.
            var resourceIds = testResources.Select(r => r.Id).Distinct().ToArray();
            var queryByIds = $"Patient?_id={string.Join(",", resourceIds)}&_tag={_fixture.FixtureTag}";

            Bundle result = await _fixture.TestFhirClient.SearchAsync(queryByIds);
            ImportTestHelper.VerifyBundle(result, expectedNonHistorical.ToArray());
        }
    }
}
