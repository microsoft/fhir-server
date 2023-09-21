// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
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
            ImportTestHelper.VerifyBundle(result, _fixture.NewImplicitVersionIdResources.Import.ToArray());

            // TODO - why is the new version not being imported? Why is this not 2?
            // Assert.Equal("2", result.Entry[0].Resource.Meta.VersionId);

            // Validate that the old version of the resource is returned by a history search.
            var queryByIdHistory = $"Patient/{resourceId}/_history";
            result = await _client.SearchAsync(queryByIdHistory);

            ImportTestHelper.VerifyBundle(
                result,
                _fixture.NewImplicitVersionIdResources.Import.Concat(_fixture.NewImplicitVersionIdResources.Existing).ToArray());
        }

        [Fact]
        public async Task GivenImportedResourceOverExisting_WhenImportVersionInConflict_ThenNotImportedWithError()
        {
            // Validate that the new version of the resource is returned by a general search.
            var resourceId = _fixture.ConflictingVersionResources.Import[0].Id;
            var queryById = $"Patient?_id={resourceId}&_tag={_fixture.FixtureTag}";
            Bundle result = await _client.SearchAsync(queryById);
            ImportTestHelper.VerifyBundle(result, _fixture.ConflictingVersionResources.Import.ToArray());

            // Only one version should be imported.
            Assert.Equal("1", result.Entry[0].Resource.Meta.VersionId);

            // Validate that the old version of the resource is returned by a history search.
            var queryByIdHistory = $"Patient/{resourceId}/_history";
            result = await _client.SearchAsync(queryByIdHistory);

            ImportTestHelper.VerifyBundle(
                result,
                _fixture.ConflictingVersionResources.Import.ToArray());
        }

        [Fact]
        public async Task GivenImportWithCreateAndDelete_WhenDeleteAfterCreate_ThenBothVersionsExistCorrectly()
        {
            // Validate that the new version of the resource is returned by a general search.
            var resourceId = _fixture.ImportAndDeleteResources.Import[0].Id;
            var queryById = $"Patient?_id={resourceId}&_tag={_fixture.FixtureTag}";
            Bundle result = await _client.SearchAsync(queryById);
            ImportTestHelper.VerifyBundle(result, _fixture.ImportAndDeleteResources.Import.ToArray());

            Assert.Equal("2", result.Entry[0].Resource.Meta.VersionId);

            // Validate that the old version of the resource is returned by a history search.
            var queryByIdHistory = $"Patient/{resourceId}/_history";
            result = await _client.SearchAsync(queryByIdHistory);

            ImportTestHelper.VerifyBundle(
                result,
                _fixture.ImportAndDeleteResources.Import.Concat(_fixture.ImportAndDeleteResources.Existing).ToArray());
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
