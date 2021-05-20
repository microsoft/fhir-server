// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Core.UnitTests.Extensions;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.SqlServer;
using Microsoft.Health.SqlServer.Configs;
using Microsoft.Health.SqlServer.Features.Schema;
using Microsoft.Health.SqlServer.Features.Schema.Manager;
using Microsoft.SqlServer.Dac.Compare;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    public class SqlServerSchemaUpgradeTests
    {
        private const string LocalConnectionString = "server=(local);Integrated Security=true";
        private const string MasterDatabaseName = "master";

        public SqlServerSchemaUpgradeTests()
        {
        }

        [Fact]
        public async Task GivenTwoSchemaInitializationMethods_WhenCreatingTwoDatabases_BothSchemasShouldBeEquivalent()
        {
            var snapshotDatabaseName = $"SNAPSHOT_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}_{BigInteger.Abs(new BigInteger(Guid.NewGuid().ToByteArray()))}";
            var diffDatabaseName = $"DIFF_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}_{BigInteger.Abs(new BigInteger(Guid.NewGuid().ToByteArray()))}";

            SqlServerFhirStorageTestHelper testHelper1 = null;
            SqlServerFhirStorageTestHelper testHelper2 = null;
            try
            {
                // Create two databases, one where we apply the the maximum supported version's snapshot SQL schema file
                (testHelper1, _) = await SetupTestHelperAndCreateDatabase(
                    snapshotDatabaseName,
                    SchemaVersionConstants.Max,
                    forceIncrementalSchemaUpgrade: false);

                // And one where we apply .diff.sql files to upgrade the schema version to the maximum supported version.
                (testHelper2, _) = await SetupTestHelperAndCreateDatabase(
                    diffDatabaseName,
                    SchemaVersionConstants.Max,
                    forceIncrementalSchemaUpgrade: true);

                bool isEqual = CompareDatabaseSchemas(snapshotDatabaseName, diffDatabaseName);
                Assert.True(isEqual);
            }
            finally
            {
                await testHelper1.DeleteDatabase(snapshotDatabaseName);
                await testHelper2.DeleteDatabase(diffDatabaseName);
            }
        }

        [Theory]
        [InlineData((int)SchemaVersion.V7)]
        [InlineData(SchemaVersionConstants.Max)]
        public async Task GivenASchemaVersion_WhenApplyingDiffTwice_ShouldSucceed(int schemaVersion)
        {
            var snapshotDatabaseName = $"SNAPSHOT_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}_{BigInteger.Abs(new BigInteger(Guid.NewGuid().ToByteArray()))}";

            SqlServerFhirStorageTestHelper testHelper = null;
            SchemaUpgradeRunner upgradeRunner;

            try
            {
                (testHelper, upgradeRunner) = await SetupTestHelperAndCreateDatabase(
                    snapshotDatabaseName,
                    schemaVersion - 1,
                    forceIncrementalSchemaUpgrade: false);

                await upgradeRunner.ApplySchemaAsync(schemaVersion, applyFullSchemaSnapshot: false, CancellationToken.None);
                await upgradeRunner.ApplySchemaAsync(schemaVersion, applyFullSchemaSnapshot: false, CancellationToken.None);
            }
            finally
            {
                await testHelper.DeleteDatabase(snapshotDatabaseName);
            }
        }

        private async Task<(SqlServerFhirStorageTestHelper testHelper, SchemaUpgradeRunner upgradeRunner)> SetupTestHelperAndCreateDatabase(string databaseName, int maxSchemaVersion, bool forceIncrementalSchemaUpgrade)
        {
            var initialConnectionString = Environment.GetEnvironmentVariable("SqlServer:ConnectionString") ?? LocalConnectionString;

            var searchService = Substitute.For<ISearchService>();
            ISearchParameterDefinitionManager defManager = new SearchParameterDefinitionManager(ModelInfoProvider.Instance, Substitute.For<IMediator>(), () => searchService.CreateMockScope(), NullLogger<SearchParameterDefinitionManager>.Instance);
            FilebasedSearchParameterStatusDataStore statusStore = new FilebasedSearchParameterStatusDataStore(defManager, ModelInfoProvider.Instance);

            var schemaInformation = new SchemaInformation(SchemaVersionConstants.Min, maxSchemaVersion);

            var connectionString = new SqlConnectionStringBuilder(initialConnectionString) { InitialCatalog = databaseName }.ToString();

            var schemaOptions = new SqlServerSchemaOptions { AutomaticUpdatesEnabled = true };
            var config = new SqlServerDataStoreConfiguration { ConnectionString = connectionString, Initialize = true, SchemaOptions = schemaOptions };
            var sqlConnectionStringProvider = new DefaultSqlConnectionStringProvider(config);
            var securityConfiguration = new SecurityConfiguration { PrincipalClaims = { "oid" } };

            SqlServerFhirModel sqlServerFhirModel = new SqlServerFhirModel(
                schemaInformation,
                defManager,
                () => statusStore,
                Options.Create(securityConfiguration),
                sqlConnectionStringProvider,
                Substitute.For<IMediator>(),
                NullLogger<SqlServerFhirModel>.Instance);

            var sqlConnectionFactory = new DefaultSqlConnectionFactory(sqlConnectionStringProvider);

            var testHelper = new SqlServerFhirStorageTestHelper(
                initialConnectionString,
                MasterDatabaseName,
                sqlServerFhirModel,
                sqlConnectionFactory);

            var scriptProvider = new ScriptProvider<SchemaVersion>();
            var baseScriptProvider = new BaseScriptProvider();
            var mediator = Substitute.For<IMediator>();

            var schemaManagerDataStore = new SchemaManagerDataStore(sqlConnectionFactory);
            var schemaUpgradeRunner = new SchemaUpgradeRunner(
                scriptProvider,
                baseScriptProvider,
                NullLogger<SchemaUpgradeRunner>.Instance,
                sqlConnectionFactory,
                schemaManagerDataStore);

            var schemaInitializer = new SchemaInitializer(
                config,
                schemaUpgradeRunner,
                schemaInformation,
                sqlConnectionFactory,
                sqlConnectionStringProvider,
                mediator,
                NullLogger<SchemaInitializer>.Instance);

            await testHelper.CreateAndInitializeDatabase(
                databaseName,
                maxSchemaVersion,
                forceIncrementalSchemaUpgrade,
                schemaInitializer);

            return (testHelper, schemaUpgradeRunner);
        }

        private bool CompareDatabaseSchemas(string databaseName1, string databaseName2)
        {
            var initialConnectionString = Environment.GetEnvironmentVariable("SqlServer:ConnectionString") ?? LocalConnectionString;

            var testConnectionString1 = new SqlConnectionStringBuilder(initialConnectionString) { InitialCatalog = databaseName1 }.ToString();
            var testConnectionString2 = new SqlConnectionStringBuilder(initialConnectionString) { InitialCatalog = databaseName2 }.ToString();

            var source = new SchemaCompareDatabaseEndpoint(testConnectionString1);
            var target = new SchemaCompareDatabaseEndpoint(testConnectionString2);
            var comparison = new SchemaComparison(source, target);

            SchemaComparisonResult result = comparison.Compare();

            // These types were introduced in earlier schema versions but are no longer used in newer versions.
            // They are not removed so as to no break compatibility with instances requiring an older schema version.
            // Exclude them from the schema comparison differences.
            (string type, string name)[] deprecatedObjectToIgnore =
            {
                ("Procedure", "[dbo].[UpsertResource]"),
                ("Procedure", "[dbo].[UpsertResource_2]"),
                ("TableType", "[dbo].[ReferenceSearchParamTableType_1]"),
                ("TableType", "[dbo].[ReferenceTokenCompositeSearchParamTableType_1]"),
                ("TableType", "[dbo].[ResourceWriteClaimTableType_1]"),
                ("TableType", "[dbo].[CompartmentAssignmentTableType_1]"),
                ("TableType", "[dbo].[ReferenceSearchParamTableType_2]"),
                ("TableType", "[dbo].[TokenSearchParamTableType_1]"),
                ("TableType", "[dbo].[TokenTextTableType_1]"),
                ("TableType", "[dbo].[StringSearchParamTableType_1]"),
                ("TableType", "[dbo].[UriSearchParamTableType_1]"),
                ("TableType", "[dbo].[NumberSearchParamTableType_1]"),
                ("TableType", "[dbo].[QuantitySearchParamTableType_1]"),
                ("TableType", "[dbo].[DateTimeSearchParamTableType_1]"),
                ("TableType", "[dbo].[ReferenceTokenCompositeSearchParamTableType_2]"),
                ("TableType", "[dbo].[TokenTokenCompositeSearchParamTableType_1]"),
                ("TableType", "[dbo].[TokenDateTimeCompositeSearchParamTableType_1]"),
                ("TableType", "[dbo].[TokenQuantityCompositeSearchParamTableType_1]"),
                ("TableType", "[dbo].[TokenStringCompositeSearchParamTableType_1]"),
                ("TableType", "[dbo].[TokenNumberNumberCompositeSearchParamTableType_1]"),
            };

            var remainingDifferences = result.Differences.Where(
                d => !deprecatedObjectToIgnore.Any(
                    i =>
                        (d.SourceObject?.ObjectType.Name == i.type && d.SourceObject?.Name?.ToString() == i.name) ||
                        (d.TargetObject?.ObjectType.Name == i.type && d.TargetObject?.Name?.ToString() == i.name)))
                .ToList();

            return remainingDifferences.Count == 0;
        }
    }
}
