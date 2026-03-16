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
