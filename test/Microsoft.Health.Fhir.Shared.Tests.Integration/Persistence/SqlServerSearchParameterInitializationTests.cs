// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.UnitTests.Extensions;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.SqlServer.Features.Client;
using Microsoft.Health.Test.Utilities;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence;

[Trait(Traits.OwningTeam, OwningTeam.Fhir)]
[Trait(Traits.Category, Categories.SearchParameterStatus)]
public class SqlServerSearchParameterInitializationTests : IClassFixture<SqlServerFhirStorageTestsFixture>
{
    private readonly SqlServerFhirStorageTestsFixture _fixture;
    private readonly ITestOutputHelper _testOutputHelper;

    public SqlServerSearchParameterInitializationTests(SqlServerFhirStorageTestsFixture fixture, ITestOutputHelper testOutputHelper)
    {
        _fixture = fixture;
        _testOutputHelper = testOutputHelper;
    }

    [Fact]
    public async Task GivenANewDatabase_WhenGettingSearchParameters_ThenNoneAreInvalid()
    {
        // Assert off base database.
        await CheckSearchParametersForInvalid();
    }

    [Fact]
    public async Task GivenADatabaseWithSearchParametersDisabled_WhenInitializing_ThenDisabledSearchParametersStayDisabled()
    {
        // Arrange
        var defaultSearchParameterStatuses = (await _fixture.SqlServerSearchParameterStatusDataStore.GetSearchParameterStatuses(CancellationToken.None)).ToList();
        var unsupportedSearchParameterUris = (await _fixture.FilebasedSearchParameterStatusDataStore.GetSearchParameterStatuses(CancellationToken.None))
            .Where(s => s.Status == SearchParameterStatus.Unsupported)
            .Select(s => s.Uri)
            .ToHashSet();
        var searchParameterToDisable = defaultSearchParameterStatuses.First(s => !unsupportedSearchParameterUris.Contains(s.Uri));
        searchParameterToDisable.Status = SearchParameterStatus.Disabled;
        var latestSearchParameter = defaultSearchParameterStatuses.MaxBy(s => s.LastUpdated);
        List<ResourceSearchParameterStatus> updatedSearchparameterStatuses = [searchParameterToDisable];
        if (latestSearchParameter.Uri != searchParameterToDisable.Uri)
        {
            updatedSearchparameterStatuses.Add(latestSearchParameter);
        }

        await _fixture.SqlServerSearchParameterStatusDataStore.UpsertStatuses(updatedSearchparameterStatuses, CancellationToken.None);

        // Act - exception will be thrown when getting status if any are null.
        await ReinitializeSearchParameterStatuses();
        var reInitializedSearchParameterStatuses = (await _fixture.SqlServerSearchParameterStatusDataStore.GetSearchParameterStatuses(CancellationToken.None)).ToList();

        // Assert
        updatedSearchparameterStatuses.ForEach(
            updatedSearchParameterStatus =>
            {
                var reInitializedSearchParameterStatus = reInitializedSearchParameterStatuses.Single(s => s.Uri == updatedSearchParameterStatus.Uri);
                Assert.Equal(updatedSearchParameterStatus.Status, reInitializedSearchParameterStatus.Status);
            });
    }

    [Fact]
    public async Task GivenSearchParametersMarkedUnsupportedInTheFile_WhenInitializing_ThenTheyRemainUnsupportedInTheDatabase()
    {
        // Arrange - collect every URI that the file-based data store marks as Unsupported.
        // These are the search parameters listed in Data/{Version}/unsupported-search-parameters.json
        // and must never be overwritten by SqlServerFhirModel.InitializeSearchParameterStatuses,
        // otherwise the server would silently return empty results (with 200 OK) for those params
        // instead of an OperationOutcome warning.
        var fileStatuses = (await _fixture.FilebasedSearchParameterStatusDataStore.GetSearchParameterStatuses(CancellationToken.None)).ToList();
        var expectedUnsupportedUris = fileStatuses
            .Where(s => s.Status == SearchParameterStatus.Unsupported)
            .Select(s => s.Uri)
            .ToList();

        Assert.NotEmpty(expectedUnsupportedUris);

        // Act - re-run the initializer. This exercises the code path that previously overwrote
        // Unsupported with Enabled/Supported on every startup.
        await ReinitializeSearchParameterStatuses();

        // Assert - every URI that came from the file as Unsupported is still Unsupported in SQL.
        var dbStatuses = (await _fixture.SqlServerSearchParameterStatusDataStore.GetSearchParameterStatuses(CancellationToken.None))
            .ToDictionary(s => s.Uri);

        foreach (var uri in expectedUnsupportedUris)
        {
            Assert.True(dbStatuses.TryGetValue(uri, out var dbStatus), $"Expected search parameter '{uri}' to exist in the SQL SearchParam table.");
            Assert.Equal(SearchParameterStatus.Unsupported, dbStatus.Status);
        }
    }

    [Fact]
    public async Task GivenASearchParameterMarkedUnsupportedInTheFileButSupportedInTheDatabase_WhenInitializing_ThenItIsMarkedUnsupportedInTheDatabase()
    {
        // Arrange
        var unsupportedSearchParameterUri = (await _fixture.FilebasedSearchParameterStatusDataStore.GetSearchParameterStatuses(CancellationToken.None))
            .First(s => s.Status == SearchParameterStatus.Unsupported);
        var databaseSearchParameter = (await _fixture.SqlServerSearchParameterStatusDataStore.GetSearchParameterStatuses(CancellationToken.None))
            .Single(s => s.Uri == unsupportedSearchParameterUri.Uri);
        databaseSearchParameter.Status = SearchParameterStatus.Supported;
        await _fixture.SqlServerSearchParameterStatusDataStore.UpsertStatuses([databaseSearchParameter], CancellationToken.None);

        // Act
        await ReinitializeSearchParameterStatuses();

        // Assert
        var reinitializedSearchParameter = (await _fixture.SqlServerSearchParameterStatusDataStore.GetSearchParameterStatuses(CancellationToken.None))
            .Single(s => s.Uri == unsupportedSearchParameterUri.Uri);
        Assert.Equal(SearchParameterStatus.Unsupported, reinitializedSearchParameter.Status);
    }

    private async Task ReinitializeSearchParameterStatuses()
    {
        await _fixture.SqlServerFhirModel.Initialize(SchemaVersionConstants.Min, CancellationToken.None);
        await _fixture.SqlServerFhirModel.Initialize(SchemaVersionConstants.Max, CancellationToken.None);
    }

    private async Task CheckSearchParametersForInvalid()
    {
        // Assert - will throw SearchParameterNotSupportedException is invalid search parameters exist.
        await _fixture.SqlServerSearchParameterStatusDataStore.GetSearchParameterStatuses(CancellationToken.None);

        // Assert again - ensure there are no null rows.
        using (SqlConnectionWrapper sqlConnectionWrapper = await _fixture.SqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(CancellationToken.None, true))
        using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateRetrySqlCommand())
        {
            sqlCommandWrapper.CommandText = @"
                SELECT *
                FROM dbo.SearchParam
                WHERE LastUpdated IS NULL OR Status IS NULL OR IsPartiallySupported IS NULL;
            ";

            using (var reader = await sqlCommandWrapper.ExecuteReaderAsync(CancellationToken.None))
            {
                if (reader.HasRows)
                {
                    Assert.Fail("Rows exist where LastUpdated IS NULL OR Status IS NULL OR IsPartiallySupported IS NULL");
                }
            }
        }
    }
}
