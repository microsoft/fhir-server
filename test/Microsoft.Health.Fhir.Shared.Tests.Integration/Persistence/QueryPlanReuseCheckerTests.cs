// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Messages.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Search;
using Microsoft.Health.Fhir.SqlServer.Registration;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Test.Utilities;
using Xunit;
using Xunit.Abstractions;
using SearchParamType = Microsoft.Health.Fhir.ValueSets.SearchParamType;
using SortOrder = Microsoft.Health.Fhir.Core.Features.Search.SortOrder;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    /// <summary>
    /// Integration tests for <see cref="QueryPlanReuseChecker"/> that validate the cache loading
    /// functionality by creating simulated skewed statistics in the database.
    /// </summary>
    [FhirStorageTestsFixtureArgumentSets(DataStore.SqlServer)]
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.DataSourceValidation)]
    public class QueryPlanReuseCheckerTests : IClassFixture<FhirStorageTestsFixture>
    {
        private const string ServiceRequestCodeSearchParamUri = "http://hl7.org/fhir/SearchParameter/clinical-code";

        private readonly FhirStorageTestsFixture _fixture;
        private readonly SqlServerFhirStorageTestsFixture _sqlFixture;
        private readonly ITestOutputHelper _output;
        private readonly FhirSqlServerConfiguration _fhirSqlConfig;

        public QueryPlanReuseCheckerTests(FhirStorageTestsFixture fixture, ITestOutputHelper testOutputHelper)
        {
            _fixture = fixture;
            _sqlFixture = (SqlServerFhirStorageTestsFixture)_fixture.Service;
            _output = testOutputHelper;

            _fhirSqlConfig = new FhirSqlServerConfiguration
            {
                EnableQueryPlanReuseChecker = true,
            };
        }

        [Fact]
        public async Task GivenQueryPlanReuseChecker_WhenInitializedWithNoSkewedStats_ThenCanReuseQueryPlanReturnsTrue()
        {
            // Arrange
            var checker = new QueryPlanReuseChecker(_sqlFixture.SqlRetryService, _fhirSqlConfig, NullLogger<QueryPlanReuseChecker>.Instance);

            // Simulate storage ready notification
            await checker.Handle(new SearchParametersInitializedNotification(), CancellationToken.None);

            // Wait for initialization to complete
            await WaitForInitializationAsync(checker);

            // Create a search option with a non-skewed parameter
            var searchParameter = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
            var searchOptions = CreateSearchOptions(new List<SearchParameterInfo> { searchParameter });

            // Act
            bool result = checker.CanReuseQueryPlan(searchOptions);

            // Assert
            Assert.True(result);
            _output.WriteLine("CanReuseQueryPlan returned true when no skewed stats exist.");
        }

        [Fact]
        public async Task GivenQueryPlanReuseChecker_WhenSearchParametersAreEmpty_ThenCanReuseQueryPlanReturnsTrue()
        {
            // Arrange
            var checker = new QueryPlanReuseChecker(_sqlFixture.SqlRetryService, _fhirSqlConfig, NullLogger<QueryPlanReuseChecker>.Instance);

            // Simulate storage ready notification
            await checker.Handle(new SearchParametersInitializedNotification(), CancellationToken.None);

            // Wait for initialization to complete
            await WaitForInitializationAsync(checker);

            var searchOptions = CreateSearchOptions(new List<SearchParameterInfo>());

            // Act
            bool result = checker.CanReuseQueryPlan(searchOptions);

            // Assert
            Assert.True(result);
            _output.WriteLine("CanReuseQueryPlan returned true for empty search parameters.");
        }

        /* These tests are currently not working because the test fixture isn't populating search parameters in the database, which causes the statistics creation to fail.
         * We need to either update the fixture to include search parameter population or mock the database interactions to properly test the cache loading logic.
         *
        [Fact]
        public async Task GivenSkewedStatisticsInDatabase_WhenCacheIsRefreshed_ThenSkewedParametersAreIdentified()
        {
            // Arrange - Insert skewed data and create statistics
            short resourceTypeId = 0;
            short searchParamId = 0;
            string statName = string.Empty;

            try
            {
                // Insert 51 ServiceRequest resources (50 with same code, 1 with different code) to create skew
                await InsertSkewedServiceRequestsAsync();

                // Get the resource type ID and search param ID for ServiceRequest code
                (resourceTypeId, searchParamId) = await GetResourceTypeAndSearchParamIdsAsync();
                _output.WriteLine($"ServiceRequest ResourceTypeId: {resourceTypeId}, Code SearchParamId: {searchParamId}");

                // Create statistics on the TokenSearchParam table for ServiceRequest code
                await CreateStatisticsAsync("TokenSearchParam", "Code", resourceTypeId, searchParamId);
                statName = $"ST_Code_WHERE_ResourceTypeId_{resourceTypeId}_SearchParamId_{searchParamId}";

                // Update statistics to reflect the skewed data
                await UpdateStatisticsAsync("TokenSearchParam");

                var checker = new QueryPlanReuseChecker(_sqlFixture.SqlRetryService, NullLogger<QueryPlanReuseChecker>.Instance);

                // Simulate storage ready notification
                await checker.Handle(new SearchParametersInitializedNotification(), CancellationToken.None);

                // Wait for initialization to complete
                await WaitForInitializationAsync(checker);

                // Use reflection to check the internal skewed parameters list
                var skewedParametersField = typeof(QueryPlanReuseChecker)
                    .GetField("_skewedParameters", BindingFlags.NonPublic | BindingFlags.Instance);
                var skewedParameters = (List<IGrouping<string, (string Uri, string ResourceTypeId)>>)skewedParametersField.GetValue(checker);

                _output.WriteLine($"Found {skewedParameters?.Count ?? 0} skewed parameter groups.");

                foreach (var group in skewedParameters ?? Enumerable.Empty<IGrouping<string, (string Uri, string ResourceTypeId)>>())
                {
                    _output.WriteLine($"Skewed parameter URI: {group.Key}");
                }
            }
            finally
            {
                // Cleanup - Delete the test resources and drop the statistic
                await CleanupTestDataAsync(resourceTypeId, searchParamId);
            }
        }

        [Fact]
        public async Task GivenDatabaseConnection_WhenGetResourceSearchParamStatsPropertiesIsCalled_ThenResultsAreReturned()
        {
            // Arrange - Insert skewed data and create statistics
            short resourceTypeId = 0;
            short searchParamId = 0;
            string statName = string.Empty;

            // Insert 51 ServiceRequest resources (50 with same code, 1 with different code) to create skew
            await InsertSkewedServiceRequestsAsync();

            // Get the resource type ID and search param ID for ServiceRequest code
            (resourceTypeId, searchParamId) = await GetResourceTypeAndSearchParamIdsAsync();
            _output.WriteLine($"ServiceRequest ResourceTypeId: {resourceTypeId}, Code SearchParamId: {searchParamId}");

            // Create statistics on the TokenSearchParam table for ServiceRequest code
            await CreateStatisticsAsync("TokenSearchParam", "Code", resourceTypeId, searchParamId);
            statName = $"ST_Code_WHERE_ResourceTypeId_{resourceTypeId}_SearchParamId_{searchParamId}";

            // Update statistics to reflect the skewed data
            await UpdateStatisticsAsync("TokenSearchParam");

            // This test verifies that the stored procedure exists and can be called
            using var connection = await _sqlFixture.SqlConnectionBuilder.GetSqlConnectionAsync(cancellationToken: CancellationToken.None);
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandType = CommandType.StoredProcedure;
            command.CommandText = "dbo.GetResourceSearchParamStatsProperties";

            try
            {
                using var reader = await command.ExecuteReaderAsync();
                int rowCount = 0;
                while (await reader.ReadAsync())
                {
                    rowCount++;
                }

                _output.WriteLine($"Total statistics rows returned: {rowCount}");
                Assert.True(rowCount > 0, "Expected at least one statistic to be returned by the stored procedure.");
            }
            catch (SqlException ex)
            {
                _output.WriteLine($"Stored procedure call failed: {ex.Message}");
                throw;
            }
        }
        */

        /// <summary>
        /// Waits for the QueryPlanReuseChecker to complete initialization.
        /// </summary>
        private async Task WaitForInitializationAsync(QueryPlanReuseChecker checker, int timeoutMs = 30000)
        {
            var isInitializedField = typeof(QueryPlanReuseChecker)
                .GetField("_isInitialized", BindingFlags.NonPublic | BindingFlags.Instance);

            int waited = 0;
            while (!(bool)isInitializedField.GetValue(checker) && waited < timeoutMs)
            {
                await Task.Delay(100);
                waited += 100;
            }

            if (waited >= timeoutMs)
            {
                Assert.Fail("Timed out waiting for QueryPlanReuseChecker to become initialized.");
            }
            else
            {
                _output.WriteLine($"QueryPlanReuseChecker initialized after {waited}ms.");
            }
        }

        // <summary>
        // Inserts 51 ServiceRequest resources with skewed code distribution.
        // 50 resources have the same code, 1 has a different code.
        // </summary>
        // private async Task InsertSkewedServiceRequestsAsync()
        // {
        //    const int commonCodeCount = 50;
        //    const string commonCode = "SKEW-COMMON-CODE";
        //    const string uniqueCode = "SKEW-UNIQUE-CODE";
        //
        //    _output.WriteLine($"Inserting {commonCodeCount + 1} ServiceRequest resources with skewed code distribution...");
        //
        //    // Insert 50 ServiceRequests with the common code
        //    for (int i = 1; i <= commonCodeCount; i++)
        //    {
        //        var serviceRequest = CreateServiceRequest($"skew-test-{i:D2}", commonCode, "Common skewed test code");
        //        await _fixture.Mediator.UpsertResourceAsync(serviceRequest.ToResourceElement());
        //    }
        //
        //   // Insert 1 ServiceRequest with a unique code to create skew
        //    var uniqueServiceRequest = CreateServiceRequest("skew-test-unique", uniqueCode, "Unique code to create skew");
        //    await _fixture.Mediator.UpsertResourceAsync(uniqueServiceRequest.ToResourceElement());
        //
        //    _output.WriteLine($"Inserted {commonCodeCount + 1} ServiceRequest resources.");
        // }

        // <summary>
        // Creates a ServiceRequest resource with the specified ID and code.
        // </summary>
        // <param name="id">The resource ID.</param>
        // <param name="code">The code value for the ServiceRequest.</param>
        // <param name="display">The display text for the code.</param>
        // <returns>A new ServiceRequest resource.</returns>
        // private static ServiceRequest CreateServiceRequest(string id, string code, string display)
        // {
        //    return new ServiceRequest
        //    {
        //        Id = $"{id}-{Guid.NewGuid().ToString("N").Substring(0, 8)}",
        //        Status = RequestStatus.Active,
        //        Intent = RequestIntent.Order,
        //        Code = new CodeableConcept
        //        {
        //            Coding = new List<Coding>
        //            {
        //                new Coding
        //                {
        //                    System = "http://snomed.info/sct",
        //                    Code = code,
        //                    Display = display,
        //                },
        //            },
        //        },
        //        Subject = new ResourceReference("Patient/example"),
        //    };
        // }

        /// <summary>
        /// Gets the ResourceTypeId for ServiceRequest and SearchParamId for the code search parameter.
        /// </summary>
        private async Task<(short ResourceTypeId, short SearchParamId)> GetResourceTypeAndSearchParamIdsAsync()
        {
            using var connection = await _sqlFixture.SqlConnectionBuilder.GetSqlConnectionAsync(cancellationToken: CancellationToken.None);
            await connection.OpenAsync();

            // Get ResourceTypeId for ServiceRequest
            short resourceTypeId;
            using (var command = connection.CreateCommand())
            {
                command.CommandType = CommandType.Text;
                command.CommandText = "SELECT ResourceTypeId FROM dbo.ResourceType WHERE Name = 'ServiceRequest'";
                var result = await command.ExecuteScalarAsync();
                resourceTypeId = Convert.ToInt16(result);
            }

            // Get SearchParamId for code search parameter
            short searchParamId;
            using (var command = connection.CreateCommand())
            {
                command.CommandType = CommandType.Text;
                command.CommandText = $"SELECT SearchParamId FROM dbo.SearchParam WHERE Uri = '{ServiceRequestCodeSearchParamUri}'";
                var result = await command.ExecuteScalarAsync();
                searchParamId = Convert.ToInt16(result);
            }

            return (resourceTypeId, searchParamId);
        }

        /// <summary>
        /// Creates statistics on the specified table for the given resource type and search parameter.
        /// </summary>
        private async Task CreateStatisticsAsync(string tableName, string columnName, short resourceTypeId, short searchParamId)
        {
            using var connection = await _sqlFixture.SqlConnectionBuilder.GetSqlConnectionAsync(cancellationToken: CancellationToken.None);
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandType = CommandType.StoredProcedure;
            command.CommandText = "dbo.CreateResourceSearchParamStats";
            command.Parameters.AddWithValue("@Table", tableName);
            command.Parameters.AddWithValue("@Column", columnName);
            command.Parameters.AddWithValue("@ResourceTypeId", resourceTypeId);
            command.Parameters.AddWithValue("@SearchParamId", searchParamId);

            try
            {
                await command.ExecuteNonQueryAsync();
                _output.WriteLine($"Created statistics on {tableName}.{columnName} for ResourceTypeId={resourceTypeId}, SearchParamId={searchParamId}");
            }
            catch (SqlException ex) when (ex.Number == 1927) // Statistics already exists
            {
                _output.WriteLine($"Statistics already exist on {tableName}.{columnName}");
            }
        }

        /// <summary>
        /// Updates statistics on the specified table to reflect current data distribution.
        /// </summary>
        private async Task UpdateStatisticsAsync(string tableName)
        {
            using var connection = await _sqlFixture.SqlConnectionBuilder.GetSqlConnectionAsync(cancellationToken: CancellationToken.None);
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandType = CommandType.Text;
            command.CommandText = $"UPDATE STATISTICS dbo.{tableName} WITH FULLSCAN";

            await command.ExecuteNonQueryAsync();
            _output.WriteLine($"Updated statistics on {tableName}");
        }

        /// <summary>
        /// Cleans up test data by deleting ServiceRequest resources and dropping the test statistic.
        /// </summary>
        private async Task CleanupTestDataAsync(short resourceTypeId, short searchParamId)
        {
            try
            {
                // Drop the statistic if it exists
                if (resourceTypeId > 0 && searchParamId > 0)
                {
                    string statName = $"ST_Code_WHERE_ResourceTypeId_{resourceTypeId}_SearchParamId_{searchParamId}";
                    using var connection = await _sqlFixture.SqlConnectionBuilder.GetSqlConnectionAsync(cancellationToken: CancellationToken.None);
                    await connection.OpenAsync();

                    using var command = connection.CreateCommand();
                    command.CommandType = CommandType.Text;
                    command.CommandText = $@"
                        IF EXISTS (SELECT * FROM sys.stats WHERE name = '{statName}' AND object_id = OBJECT_ID('dbo.TokenSearchParam'))
                        DROP STATISTICS dbo.TokenSearchParam.[{statName}]";

                    await command.ExecuteNonQueryAsync();
                    _output.WriteLine($"Dropped test statistic: {statName}");
                }
            }
            catch (SqlException ex)
            {
                _output.WriteLine($"Warning: Could not clean up test data: {ex.Message}");
            }
        }

        /// <summary>
        /// Creates a SearchOptions instance with the specified search parameters.
        /// </summary>
        private SearchOptions CreateSearchOptions(IReadOnlyList<SearchParameterInfo> searchParameters)
        {
            return new SearchOptions
            {
                Sort = Array.Empty<(SearchParameterInfo, SortOrder)>(),
                SearchParameters = searchParameters,
                ResourceVersionTypes = ResourceVersionType.Latest,
            };
        }
    }
}
