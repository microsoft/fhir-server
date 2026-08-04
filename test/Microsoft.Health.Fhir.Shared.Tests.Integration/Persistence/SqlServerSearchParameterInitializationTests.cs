// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Medino;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.UnitTests.Extensions;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.JobManagement;
using Microsoft.Health.SqlServer.Features.Client;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence;

[Trait(Traits.OwningTeam, OwningTeam.Fhir)]
[Trait(Traits.Category, Categories.SearchParameterStatus)]
public class SqlServerSearchParameterInitializationTests : IClassFixture<SqlServerFhirStorageTestsFixture>
{
    private const string LocationNearUri = "http://hl7.org/fhir/SearchParameter/Location-near";

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
        List<ResourceSearchParameterStatus> updatedSearchparameterStatuses = [];

        // Disable every 5th search parameter
        for (int i = 0; i < defaultSearchParameterStatuses.Count; i++)
        {
            if ((i + 1) % 5 == 0)
            {
                defaultSearchParameterStatuses[i].Status = SearchParameterStatus.Disabled;
                updatedSearchparameterStatuses.Add(defaultSearchParameterStatuses[i]);
            }
        }

        await _fixture.SqlServerSearchParameterStatusDataStore.UpsertStatuses(updatedSearchparameterStatuses, CancellationToken.None);

        // Act - exception will be thrown when getting status if any are null.
        await _fixture.SqlServerFhirModel.Initialize(SchemaVersionConstants.Max, CancellationToken.None);
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
        await _fixture.SqlServerFhirModel.Initialize(SchemaVersionConstants.Max, CancellationToken.None);

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
    public async Task GivenAnAgedDatabaseWhereAnUnsupportedSearchParameterIsStoredAsEnabled_WhenInitializing_ThenItIsRepairedToUnsupported()
    {
        // Arrange - reproduce the state that databases seeded between 2026-03-06 (PR #5403) and 2026-07-23
        // (PR #5684) are left in. PR #5403 overwrote the Unsupported status that comes from
        // unsupported-search-parameters.json with Enabled/Supported, and because SqlServerFhirModel drops every
        // file status whose stored status has moved past Initialized, the guard added by PR #5684 never runs for
        // rows that are already seeded. On such a database Location-near is reported as searchable, reaches the
        // expression parser and produces a 500 instead of a NotSupported OperationOutcome warning.
        var fileStatuses = (await _fixture.FilebasedSearchParameterStatusDataStore.GetSearchParameterStatuses(CancellationToken.None)).ToList();
        var locationNear = fileStatuses.SingleOrDefault(s => s.Uri.OriginalString == LocationNearUri);

        Assert.NotNull(locationNear);
        Assert.Equal(SearchParameterStatus.Unsupported, locationNear.Status);

        // A second parameter that the file also marks Unsupported is stored as Disabled. Disabled can only be set
        // deliberately (by $status or a delete), so the repair must leave it alone.
        var untouchedUri = fileStatuses
            .First(s => s.Status == SearchParameterStatus.Unsupported && s.Uri.OriginalString != LocationNearUri)
            .Uri;

        await SeedStoredStatusAsync(locationNear.Uri, SearchParameterStatus.Enabled);
        await SeedStoredStatusAsync(untouchedUri, SearchParameterStatus.Disabled);

        try
        {
            // Act - simulate a service restart against the existing database.
            await CreateSqlServerFhirModel().Initialize(SchemaVersionConstants.Max, CancellationToken.None);

            // Assert
            var dbStatuses = (await _fixture.SqlServerSearchParameterStatusDataStore.GetSearchParameterStatuses(CancellationToken.None))
                .ToDictionary(s => s.Uri);

            Assert.Equal(SearchParameterStatus.Unsupported, dbStatuses[locationNear.Uri].Status);
            Assert.Equal(SearchParameterStatus.Disabled, dbStatuses[untouchedUri].Status);
        }
        finally
        {
            await SeedStoredStatusAsync(untouchedUri, SearchParameterStatus.Unsupported);
            await RestoreUniformLastUpdatedAsync();
        }
    }

    [Fact]
    public async Task GivenADatabaseWhereSearchParameterStatusesAreAlreadyCorrect_WhenInitializing_ThenNothingIsRewritten()
    {
        // Arrange - the repair added for the aged-database case must be idempotent: once a row has been written
        // back to Unsupported, every subsequent startup has to be a no-op, otherwise every replica restart would
        // merge rows and bump LastUpdated, forcing an unnecessary search parameter cache refresh everywhere.
        await CreateSqlServerFhirModel().Initialize(SchemaVersionConstants.Max, CancellationToken.None);

        var before = (await _fixture.SqlServerSearchParameterStatusDataStore.GetSearchParameterStatuses(CancellationToken.None))
            .ToDictionary(s => s.Uri, s => (s.Status, s.LastUpdated));

        // Act
        await CreateSqlServerFhirModel().Initialize(SchemaVersionConstants.Max, CancellationToken.None);

        // Assert - dbo.MergeSearchParams stamps a fresh LastUpdated on every row it merges, so an unchanged
        // LastUpdated proves that no merge happened at all.
        var after = (await _fixture.SqlServerSearchParameterStatusDataStore.GetSearchParameterStatuses(CancellationToken.None))
            .ToDictionary(s => s.Uri, s => (s.Status, s.LastUpdated));

        Assert.Equal(before.Count, after.Count);

        foreach (var entry in before)
        {
            Assert.True(after.TryGetValue(entry.Key, out var actual), $"Expected search parameter '{entry.Key}' to still exist in the SQL SearchParam table.");
            Assert.Equal(entry.Value.Status, actual.Status);
            Assert.Equal(entry.Value.LastUpdated, actual.LastUpdated);
        }
    }

    [Fact]
    public async Task GivenADatabaseNeedingRepairAndAReindexJobInProgress_WhenInitializing_ThenTheRepairIsDeferredAndInitializationSucceeds()
    {
        // Arrange - dbo.MergeSearchParams throws error 50002 whenever an active QueueType 6 job exists. Before the
        // repair existed an aged database in steady state had nothing to merge and never reached the sproc, so 50002
        // was unreachable from startup. Now an affected database calls the sproc on every startup until the repair
        // lands, and an unhandled 50002 would stop SqlServerFhirModel.Initialize before it sets
        // _highestInitializedVersion and publishes StorageInitializedNotification - leaving every replica unable to
        // finish storage initialization for as long as the reindex runs.
        var fileStatuses = (await _fixture.FilebasedSearchParameterStatusDataStore.GetSearchParameterStatuses(CancellationToken.None)).ToList();
        Uri locationNear = fileStatuses.Single(s => s.Uri.OriginalString == LocationNearUri).Uri;

        await SeedStoredStatusAsync(locationNear, SearchParameterStatus.Enabled);

        long groupId = await EnqueueReindexJobAsync();

        try
        {
            try
            {
                // Act
                await CreateSqlServerFhirModel().Initialize(SchemaVersionConstants.Max, CancellationToken.None);

                // Assert - initialization completed, and the repair was deferred rather than applied.
                var deferred = (await _fixture.SqlServerSearchParameterStatusDataStore.GetSearchParameterStatuses(CancellationToken.None))
                    .Single(s => s.Uri == locationNear);

                Assert.Equal(SearchParameterStatus.Enabled, deferred.Status);
            }
            finally
            {
                await RemoveReindexJobsAsync(groupId);
            }

            // Assert - the next startup after the reindex finishes performs the deferred repair.
            await CreateSqlServerFhirModel().Initialize(SchemaVersionConstants.Max, CancellationToken.None);

            var repaired = (await _fixture.SqlServerSearchParameterStatusDataStore.GetSearchParameterStatuses(CancellationToken.None))
                .Single(s => s.Uri == locationNear);

            Assert.Equal(SearchParameterStatus.Unsupported, repaired.Status);
        }
        finally
        {
            await RestoreUniformLastUpdatedAsync();
        }
    }

    /// <summary>
    /// Writes a status directly to dbo.SearchParam so that a test can start from a database state that only a
    /// long-lived (rather than a freshly deployed) database would ever be in.
    /// </summary>
    private async Task SeedStoredStatusAsync(Uri uri, SearchParameterStatus status)
    {
        var dbStatuses = (await _fixture.SqlServerSearchParameterStatusDataStore.GetSearchParameterStatuses(CancellationToken.None)).ToList();
        var target = dbStatuses.Single(s => s.Uri == uri);

        target.Status = status;

        // dbo.MergeSearchParams treats the highest LastUpdated in the input as an optimistic concurrency token and
        // fails with error 50001 unless it matches the current maximum in dbo.SearchParam.
        target.LastUpdated = dbStatuses.Max(s => s.LastUpdated);

        await _fixture.SqlServerSearchParameterStatusDataStore.UpsertStatuses(new[] { target }, CancellationToken.None);
    }

    /// <summary>
    /// Creates a new <see cref="SqlServerFhirModel"/> over the fixture's database. The fixture's own instance has
    /// already initialized to the current schema version and short-circuits on subsequent calls, so a new instance
    /// is what makes a test behave like a service starting up against an existing database.
    /// </summary>
    private SqlServerFhirModel CreateSqlServerFhirModel()
    {
        var searchParameterDefinitionManager = (ISearchParameterDefinitionManager)((IServiceProvider)_fixture).GetService(typeof(SearchParameterDefinitionManager));

        return new SqlServerFhirModel(
            _fixture.SchemaInformation,
            searchParameterDefinitionManager,
            () => _fixture.FilebasedSearchParameterStatusDataStore,
            Options.Create(new SecurityConfiguration { PrincipalClaims = { "oid" } }),
            _fixture.SqlConnectionWrapperFactory.CreateMockScopeProvider(),
            Substitute.For<IMediator>(),
            _fixture.SqlRetryService,
            NullLogger<SqlServerFhirModel>.Instance);
    }

    /// <summary>
    /// Re-stamps every row in dbo.SearchParam with a single LastUpdated value. dbo.MergeSearchParams only touches the
    /// rows it merges, so seeding and repairing individual rows leaves the table with a mix of LastUpdated values.
    /// Other tests in this class upsert a subset of statuses, which the stored procedure only accepts when that subset
    /// carries the current maximum, so tests that mutate individual rows restore the uniform state they inherited.
    /// </summary>
    private async Task RestoreUniformLastUpdatedAsync()
    {
        var dbStatuses = (await _fixture.SqlServerSearchParameterStatusDataStore.GetSearchParameterStatuses(CancellationToken.None)).ToList();
        var maxLastUpdated = dbStatuses.Max(s => s.LastUpdated);

        dbStatuses.ForEach(s => s.LastUpdated = maxLastUpdated);

        await _fixture.SqlServerSearchParameterStatusDataStore.UpsertStatuses(dbStatuses, CancellationToken.None);
    }

    /// <summary>
    /// Enqueues a reindex job so that dbo.GetActiveJobs reports a reindex in progress, which is what makes
    /// dbo.MergeSearchParams reject search parameter changes with error 50002.
    /// </summary>
    private async Task<long> EnqueueReindexJobAsync()
    {
        var queueClient = (IQueueClient)((IServiceProvider)_fixture).GetService(typeof(IQueueClient));

        IReadOnlyList<JobInfo> jobs = await queueClient.EnqueueAsync(
            (byte)QueueType.Reindex,
            new[] { "{\"typeId\":1}" },
            groupId: null,
            forceOneActiveJobGroup: false,
            CancellationToken.None);

        return jobs[0].GroupId;
    }

    private async Task RemoveReindexJobsAsync(long groupId)
    {
        using SqlConnectionWrapper sqlConnectionWrapper = await _fixture.SqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(CancellationToken.None, true);
        using SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateRetrySqlCommand();

        sqlCommandWrapper.CommandText = "DELETE FROM dbo.JobQueue WHERE QueueType = @QueueType AND GroupId = @GroupId";
        sqlCommandWrapper.Parameters.AddWithValue("@QueueType", (byte)QueueType.Reindex);
        sqlCommandWrapper.Parameters.AddWithValue("@GroupId", groupId);

        await sqlCommandWrapper.ExecuteNonQueryAsync(CancellationToken.None);
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
